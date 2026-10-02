"""CPU-only V3 task/polarity/matter balanced curriculum and checkpoint proof.

No JSONL edits or duplicated records. Oversampling is exposure, not new gold.
SHA-counter shuffle is independent of Python's random implementation/version.
Persist CONSUMED sample state, never a prefetched DataLoader iterator position.
Trainer integration and GPU continuation proof are a separate pre-launch gate.
"""
from collections import Counter, defaultdict
from copy import deepcopy
import hashlib
import json
import math

VERSION = 'task-matter-polarity-sha256-v3.1'
WEIGHTS = {
    'attribution_classification': 6,
    'multi_order_current_position': 6,
    'office_action_detection': 6,
    'compliance_state': 3,
    'semantic_proposition_extraction': 3,
    'important_fact_selection': 3,
    'date_specific_retrieval_or_QA': 2,
    'order_digest': 2,
}
BALANCED_TASKS = {'office_action_detection', 'date_specific_retrieval_or_QA'}


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':'))


class Curriculum:
    def __init__(self, rows, seed, weights=None):
        if isinstance(seed, bool) or not isinstance(seed, int):
            raise ValueError('Integer curriculum seed required')
        self.seed = seed
        self.weights = dict(WEIGHTS if weights is None else weights)
        if not self.weights or any(isinstance(v, bool) or not isinstance(v, int) or v < 1
                                   for v in self.weights.values()):
            raise ValueError('Positive integer task weights required')
        self.rows = {r['id']: deepcopy(r) for r in rows}
        if not rows or len(self.rows) != len(rows):
            raise ValueError('Nonempty unique original record IDs required')
        self.buckets = defaultdict(lambda: defaultdict(list))
        for row in self.rows.values():
            if row['task'] not in self.weights or not row.get('matter_id'):
                raise ValueError('Unknown task or missing matter identity')
            target = row.get('expected', row.get('target'))
            if not isinstance(target, dict) or not (('claims' in target) ^ ('facts' in target)):
                raise ValueError('Original reviewed target contract required')
            selected = target.get('claims', target.get('facts'))
            if not isinstance(selected, list):
                raise ValueError('Invalid original selection target')
            polarity = 'positive' if selected else 'empty'
            self.buckets[(row['task'], polarity)][row['matter_id']].append(row['id'])
        tasks = {r['task'] for r in rows}
        if tasks != set(self.weights):
            raise ValueError('Every declared weighted task must have audited examples')
        for matters in self.buckets.values():
            for ids in matters.values():
                ids.sort()
        self.fingerprint = hashlib.sha256(canonical(sorted(self.rows.values(), key=lambda r: r['id'])).encode()).hexdigest()
        self.counter = 0
        self.index = 0
        self.last_matter = None
        self.task_deck = []
        self.polarity_decks = {}
        self.matter_decks = {}
        self.example_decks = {}

    def _shuffle(self, values):
        values = list(values)
        for i in range(len(values) - 1, 0, -1):
            digest = hashlib.sha256(f'{VERSION}:{self.seed}:{self.counter}'.encode()).digest()
            self.counter += 1
            j = int.from_bytes(digest, 'big') % (i + 1)
            values[i], values[j] = values[j], values[i]
        return values

    def next_id(self):
        if not self.task_deck:
            self.task_deck = self._shuffle([task for task, weight in sorted(self.weights.items())
                                             for _ in range(weight)])
        task = self.task_deck.pop()
        polarities = [p for p in ('positive', 'empty') if (task, p) in self.buckets]
        if not self.polarity_decks.get(task):
            self.polarity_decks[task] = self._shuffle([
                p for p in polarities for _ in range(
                    3 if task in BALANCED_TASKS and p == 'positive' else 1)])
        polarity = self.polarity_decks[task].pop()
        bucket = (task, polarity)
        key = task + '|' + polarity
        matters = self.buckets[bucket]
        if not self.matter_decks.get(key):
            self.matter_decks[key] = self._shuffle(sorted(matters))
        deck = self.matter_decks[key]
        # At a refill boundary the only leftover matter may equal last_matter.
        # Avoid that streak when another independent matter exists; do not add
        # fake records or silently change the selected task/polarity exposure.
        if deck[-1] == self.last_matter and len(matters) > 1:
            alternatives = [m for m in deck if m != self.last_matter]
            if alternatives:
                offset = deck.index(alternatives[-1])
                deck[-1], deck[offset] = deck[offset], deck[-1]
                matter = deck.pop()
            else:
                matter = self._shuffle(sorted(m for m in matters if m != self.last_matter))[0]
                deck.pop()  # boundary repeat replaced by a genuine other matter
        else:
            matter = deck.pop()
        example_key = key + '|' + matter
        if not self.example_decks.get(example_key):
            self.example_decks[example_key] = self._shuffle(matters[matter])
        result = self.example_decks[example_key].pop()
        self.last_matter = matter
        self.index += 1
        return result

    def snapshot(self):
        return deepcopy({'version': VERSION, 'seed': self.seed,
                         'dataset_sha256': self.fingerprint, 'weights': self.weights,
                         'logical_sample_index': self.index, 'shuffle_counter': self.counter,
                         'last_matter': self.last_matter, 'task_deck': self.task_deck,
                         'polarity_decks': self.polarity_decks,
                         'matter_decks': self.matter_decks, 'example_decks': self.example_decks})

    @classmethod
    def resume(cls, rows, state):
        # Checkpoint replay binds all task/matter/PRNG state, not merely seed.
        # This is cheap for the deliberately bounded pilot stream.
        if state.get('version') != VERSION:
            raise ValueError('Unknown sampler version')
        index = state.get('logical_sample_index')
        if isinstance(index, bool) or not isinstance(index, int) or not 0 <= index <= 100000:
            raise ValueError('Invalid/unbounded consumed sample count')
        result = cls(rows, state['seed'], state['weights'])
        if result.fingerprint != state['dataset_sha256']:
            raise ValueError('Resume dataset differs from original gold')
        for _ in range(index):
            result.next_id()
        if result.snapshot() != state:
            raise ValueError('Checkpoint curriculum state does not match consumed sample index')
        return result


def consumed_checkpoint(rows, seed, consumed_samples, weights=None):
    """Call with actual consumed microbatch count, not optimizer-applied updates.

    FP16 scaler skips still consume examples. Prefetch must not advance this state.
    A single-device, batch-one, fixed-accumulation Trainer can derive the consumed
    count from trainer global step ONLY after a complete accumulation boundary.
    """
    if isinstance(consumed_samples, bool) or not isinstance(consumed_samples, int) or not 0 <= consumed_samples <= 100000:
        raise ValueError('Invalid consumed sample count')
    stream = Curriculum(rows, seed, weights)
    for _ in range(consumed_samples):
        stream.next_id()
    return stream.snapshot()


def exposure_report(rows, seed, sample_count, weights=None):
    if not isinstance(sample_count, int) or not 1 <= sample_count <= 100000:
        raise ValueError('Bounded positive exposure count required')
    stream = Curriculum(rows, seed, weights)
    tasks, pools, matters, seen = Counter(), Counter(), Counter(), set()
    repeats = 0
    previous = None
    for _ in range(sample_count):
        key = stream.next_id()
        row = stream.rows[key]
        tasks[row['task']] += 1
        target = row.get('expected', row.get('target'))
        polarity = 'positive' if target.get('claims', target.get('facts')) else 'empty'
        pools[row['task'] + '|' + polarity] += 1
        matters[row['matter_id']] += 1
        repeats += previous == row['matter_id']
        previous = row['matter_id']
        seen.add(key)
    return {'sampler_version': VERSION, 'seed': seed, 'dataset_sha256': stream.fingerprint,
            'weights': stream.weights, 'original_record_count': len(rows),
            'logical_exposures': sample_count, 'equivalent_dataset_passes': sample_count / len(rows),
            'unique_records_exposed': len(seen), 'task_exposures': dict(tasks),
            'positive_empty_exposures': dict(pools), 'matter_exposures': dict(matters),
            'consecutive_same_matter': repeats}


def calculate_budget(example_count, gradient_accumulation, equivalent_passes, max_updates=240):
    """No hardcoded 80 steps. Explicit ceiling fails rather than silently truncates."""
    if (isinstance(example_count, bool) or not isinstance(example_count, int) or example_count < 1
            or gradient_accumulation not in range(1, 9)
            or not math.isfinite(equivalent_passes) or not 0 < equivalent_passes <= 4
            or not isinstance(max_updates, int) or not 1 <= max_updates <= 240):
        raise ValueError('Invalid pilot update-budget inputs')
    updates = math.ceil(example_count * equivalent_passes / gradient_accumulation)
    if updates > max_updates:
        raise ValueError('Requested exposure exceeds explicit bounded update budget')
    exposures = updates * gradient_accumulation
    return {'original_examples': example_count, 'gradient_accumulation': gradient_accumulation,
            'batch_size': 1, 'devices': 1, 'requested_equivalent_passes': equivalent_passes,
            'trainer_steps': updates, 'logical_exposures': exposures,
            'equivalent_dataset_passes': exposures / example_count,
            'applied_optimizer_updates': 'Must be measured; scaler skips may occur'}
