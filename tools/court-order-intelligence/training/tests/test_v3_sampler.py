import copy
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from v3_sampler import Curriculum, WEIGHTS, consumed_checkpoint, exposure_report, calculate_budget


def fixtures():
    # Software fixtures, never training gold or sources.
    return [{'id': f'{task}-{matter}-{polarity}-{n}', 'task': task,
             'matter_id': f'matter-{matter}', 'target': {'claims': [{'factId': 0}] if polarity else []}}
            for task in WEIGHTS for matter in range(4) for polarity in (0, 1) for n in range(2)]


class V3SamplerTests(unittest.TestCase):
    def test_seeded_shuffle_reproducible_and_different_seeds_differ(self):
        rows = fixtures()
        a, b, c = (Curriculum(rows, seed) for seed in (73, 73, 74))
        first = [a.next_id() for _ in range(320)]
        self.assertEqual(first, [b.next_id() for _ in range(320)])
        self.assertNotEqual(first, [c.next_id() for _ in range(320)])

    def test_continuous_80_steps_vs_resumed_steps_exact_logical_sequence(self):
        rows = fixtures()
        continuous = Curriculum(rows, 73)
        expected = [continuous.next_id() for _ in range(80 * 4)]
        for checkpoint_step in (1, 10, 37, 79):
            consumed = checkpoint_step * 4
            state = consumed_checkpoint(rows, 73, consumed)
            persisted = json.loads(json.dumps(state))
            resumed = Curriculum.resume(rows, persisted)
            actual = [resumed.next_id() for _ in range(consumed, 80 * 4)]
            self.assertEqual(actual, expected[consumed:])
            self.assertEqual(resumed.snapshot(), continuous.snapshot())

    def test_prefetch_does_not_determine_consumed_resume_position(self):
        rows = fixtures()
        generator = Curriculum(rows, 73)
        for _ in range(49): generator.next_id()
        saved = consumed_checkpoint(rows, 73, 40)
        resumed = Curriculum.resume(rows, saved)
        reference = Curriculum(rows, 73)
        expected = [reference.next_id() for _ in range(41)][40]
        self.assertEqual(resumed.next_id(), expected)
        self.assertNotEqual(saved['logical_sample_index'], generator.snapshot()['logical_sample_index'])

    def test_complete_task_decks_exact_weights_and_positive_exposure(self):
        rows = fixtures()
        report = exposure_report(rows, 73, sum(WEIGHTS.values()) * 40)
        self.assertEqual(report['task_exposures'], {t: w * 40 for t, w in WEIGHTS.items()})
        for task in ('office_action_detection', 'date_specific_retrieval_or_QA'):
            pools = report['positive_empty_exposures']
            self.assertEqual(pools[task + '|positive'], 3 * pools[task + '|empty'])
        self.assertEqual(report['consecutive_same_matter'], 0)

    def test_no_mutation_fake_records_or_input_order_dependency(self):
        rows = fixtures()
        before = copy.deepcopy(rows)
        a, b = Curriculum(rows, 73), Curriculum(list(reversed(rows)), 73)
        ids = [a.next_id() for _ in range(1000)]
        self.assertEqual(ids, [b.next_id() for _ in range(1000)])
        self.assertTrue(set(ids).issubset({r['id'] for r in rows}))
        self.assertEqual(rows, before)

    def test_tampered_dataset_seed_counter_or_matter_state_cannot_resume(self):
        rows = fixtures()
        state = consumed_checkpoint(rows, 73, 40)
        for key, value in (('seed', 74), ('shuffle_counter', 0), ('last_matter', 'wrong'),
                           ('logical_sample_index', 41), ('version', 'unknown')):
            with self.subTest(key=key), self.assertRaises(ValueError):
                Curriculum.resume(rows, dict(state, **{key: value}))
        changed = copy.deepcopy(rows)
        changed[0]['target']['claims'] = [{'factId': 1}]
        with self.assertRaises(ValueError): Curriculum.resume(changed, state)

    def test_missing_task_bucket_duplicate_ids_and_unbounded_resume_fail(self):
        rows = fixtures()
        for bad in (rows + [rows[0]], rows[16:], []):
            with self.assertRaises(ValueError): Curriculum(bad, 73)
        with self.assertRaises(ValueError): consumed_checkpoint(rows, 73, 100001)

    def test_budget_derived_from_final_counts_not_hardcoded_80(self):
        result = calculate_budget(300, 4, 2)
        self.assertEqual(result['trainer_steps'], 150)
        self.assertEqual(result['logical_exposures'], 600)
        self.assertEqual(result['equivalent_dataset_passes'], 2)
        with self.assertRaises(ValueError): calculate_budget(1000, 4, 4)
        self.assertEqual(calculate_budget(301, 4, 2)['trainer_steps'], 151)


if __name__ == '__main__':
    unittest.main()
