"""Offline task-gold assertions, with no HTTP, GPU or model-generated targets."""
import copy
import json
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent))
from annotations import v2_tasks
from foundation import compile_example
from validate_gold import validate_example
from audit_v2_foundation import protected_matters


class V2TaskSafety(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        ledger = json.loads((ROOT / 'pilot-v2/foundation-audit.json').read_text(encoding='utf-8'))['ledger']
        sources = {v: {'matter_id': s['case'], 'order_date': s['order_date'],
                       'page_count': s['pages'], 'sha256': s['sha256'], 'url': s['url']}
                   for v, s in ledger.items()}
        cls.corpus = SimpleNamespace(SOURCES=sources, PASSAGES=v2_tasks.PASSAGES,
                                    EXAMPLES=v2_tasks.EXAMPLES, REVIEW=v2_tasks.REVIEW,
                                    VERIFIED_IDS=v2_tasks.VERIFIED_IDS)
        cls.examples = {a[0]: compile_example(a, cls.corpus) for a in v2_tasks.EXAMPLES}

    def test_every_authored_target_validates_before_runtime_gate(self):
        for e in self.examples.values():
            with self.subTest(id=e['id']):
                validate_example(e, self.corpus)

    def test_reserved_and_old_protected_matters_never_train(self):
        protected = protected_matters()
        for e in self.examples.values():
            self.assertNotIn(e['matter_id'], protected)

    def test_current_position_has_three_actual_distinct_orders(self):
        for e in self.examples.values():
            if e['task'] == 'multi_order_current_position':
                self.assertEqual(len({p['order_date'] for p in e['provenance']}), 3)
                self.assertEqual(len({p['matter_id'] for p in e['provenance']}), 1)

    def test_renewed_action_not_three_simultaneous_expired_actions(self):
        decision = v2_tasks.CHAIN_DECISIONS['4806-active-action']
        self.assertEqual(decision[1], ['wpc4806-2014-p2', 'wpc4806-2014-p3'])
        self.assertTrue(all(v2_tasks.PASSAGES[p][0] == 'wpc4806-2014' for p in decision[1]))

    def test_conditional_cost_source_is_not_rewritten(self):
        passage = v2_tasks.PASSAGES['wpc4806-2014-p3'][2]
        self.assertIn('If', passage)
        self.assertIn('10,000', passage)

    def test_counsel_promises_and_repeated_time_are_not_compliance(self):
        self.assertEqual(v2_tasks.CHAIN_DECISIONS['4806-compliance'][1], [])
        for version in ['wpc4806-2014-feb2020', 'wpc4806-2014-apr2022', 'wpc4806-2014']:
            self.assertEqual(self.examples[version + '-compliance']['target']['claims'], [])

    def test_blank_template_performance_is_not_lac_filled_template_compliance(self):
        selected = v2_tasks.CHAIN_DECISIONS['6108-compliance'][1]
        self.assertEqual(selected, ['wpc6108-2015-feb25-p1'])
        self.assertNotIn('wpc6108-2015-p0', selected)
        self.assertNotIn('wpc6108-2015-p1', selected)

    def test_actions_require_explicit_current_lac_actor(self):
        positive = 0
        for annotation in v2_tasks.EXAMPLES:
            if annotation[1] == 'office_action_detection':
                for pid in annotation[3]:
                    _, _, _, role, _, scope, actor = v2_tasks.PASSAGES[pid]
                    self.assertEqual((role, scope, actor), ('COURT_DIRECTION', 'Current', 'LAC'))
                positive += bool(annotation[3])
        self.assertGreaterEqual(positive, 10)

    def test_generic_respondents_are_not_specific_lac_action(self):
        self.assertEqual(v2_tasks.CHAIN_DECISIONS['13932-current-obligation'][1], [])

    def test_unsupported_all_paid_all_done_is_insufficient(self):
        for name, e in self.examples.items():
            if name.endswith('-unsupported-completion'):
                self.assertEqual(e['target']['claims'], [])
                self.assertEqual(e['outcome'], 'INSUFFICIENT_EVIDENCE')

    def test_tampered_claim_target_rejected(self):
        e = copy.deepcopy(self.examples['4806-compliance'])
        e['target']['claims'] = self.examples['4806-active-action']['target']['claims']
        with self.assertRaises(ValueError):
            validate_example(e, self.corpus)

    def test_partial_export_blocks_training_and_reports_oracle_inputs(self):
        stats = json.loads((ROOT / 'pilot-v2/task-foundation-stats.json').read_text(encoding='utf-8'))
        self.assertFalse(stats['gpu_launch_allowed'])
        self.assertFalse(stats['protected_splits_frozen'])
        self.assertTrue(stats['runtime_claims_inputs_are_reviewed_structured_evidence_not_end_to_end_extraction'])
        quarantined = json.loads((ROOT / 'pilot-v2/task-quarantine.json').read_text(encoding='utf-8'))
        exported = {json.loads(line)['id'] for line in (ROOT / 'pilot-v2/train-foundation.jsonl').read_text(encoding='utf-8').splitlines()}
        for item in quarantined:
            self.assertFalse(item['truthful_labels_changed'])
            self.assertNotIn(item['id'], exported)


if __name__ == '__main__':
    unittest.main()
