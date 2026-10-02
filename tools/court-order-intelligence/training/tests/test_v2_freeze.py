"""Frozen public gold checks. Never performs inference or accesses credentials."""
import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent))
from freeze_pilot_v2 import verify_frozen, check_v1_passages
from annotations import v2_eval_review, v2_tasks
from leakage import require_one_split
from audit_v2_context import check


class V2FrozenSafety(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = verify_frozen(ROOT / 'pilot-v2')
        cls.sets = {s: [json.loads(line) for line in (ROOT / 'pilot-v2' / (s + '.jsonl')).read_text(encoding='utf-8').splitlines()]
                    for s in ('train', 'validation', 'blind')}

    def test_review_and_split_checksums_are_frozen(self):
        self.assertTrue(self.manifest['protected_splits_frozen'])
        self.assertFalse(self.manifest['post_blind_tuning_allowed'])

    def test_no_matter_crosses_splits(self):
        seen = set()
        for rows in self.sets.values():
            matters = {e['matter_id'] for e in rows}
            self.assertFalse(seen & matters)
            seen |= matters

    def test_leakage_groups_are_split_isolated(self):
        rows = sum(self.sets.values(), [])
        require_one_split({e['id']: e['leakage_group'] for e in rows}, {e['id']: e['split'] for e in rows})

    def test_cross_group_split_rejected(self):
        with self.assertRaises(ValueError):
            require_one_split({'a': 'g', 'b': 'g'}, {'a': 'train', 'b': 'blind'})

    def test_twelve_positions_are_four_real_chains_not_twelve_matters(self):
        rows = [e for e in self.sets['train'] if e['task'] == 'multi_order_current_position']
        self.assertEqual(len(rows), 12)
        self.assertEqual(len({e['matter_id'] for e in rows}), 4)
        for e in rows:
            self.assertEqual(len({p['order_date'] for p in e['provenance']}), 3)

    def test_protected_gold_never_train(self):
        train_ids = {e['id'] for e in self.sets['train']}
        self.assertFalse(train_ids & v2_eval_review.VERIFIED_IDS)
        self.assertTrue(train_ids <= v2_tasks.VERIFIED_IDS)

    def test_quarantine_never_exported(self):
        audit = json.loads((ROOT / 'pilot-v2/pre-freeze-audit.json').read_text())
        ids = {e['id'] for rows in self.sets.values() for e in rows}
        for q in audit['quarantined']:
            self.assertNotIn(q.get('id'), ids)

    def test_all_prompt_and_training_contexts_fit_unchanged_cap(self):
        context = json.loads((ROOT / 'pilot-v2/all-splits-context-audit.json').read_text())
        self.assertEqual(context['examples_checked'], sum(map(len, self.sets.values())))
        self.assertTrue(all(r['training_fits'] and r['existing_pilot_inference_prompt_fits'] for r in context['results']))
        self.assertFalse(context['evidence_truncated'])

    def test_v1_related_passage_never_becomes_training(self):
        sample = {'matter_id': 'new', 'split': 'train', 'provenance': [{'text': 'Exact protected directive.'}]}
        with self.assertRaises(ValueError):
            check_v1_passages([sample], {'blind': [{'provenance': sample['provenance']}]})

    def test_training_possession_target_is_actual_sdm_report_not_prayer(self):
        supplied, selected, *_ = v2_tasks.CHAIN_DECISIONS['9093-position']
        self.assertIn('wpc9093-2022-feb09-p0', selected)
        self.assertNotIn('wpc9093-2022-feb09-p1', selected)


if __name__ == '__main__':
    unittest.main()
