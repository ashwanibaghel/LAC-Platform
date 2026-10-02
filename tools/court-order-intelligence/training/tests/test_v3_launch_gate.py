import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from v3_launch_gate import MINIMUMS, REQUIRED_PROOFS, assess, require_go


class LaunchGateTests(unittest.TestCase):
    def complete_fixture(self):
        return dict(MINIMUMS, **{key: True for key in REQUIRED_PROOFS})

    def test_missing_evidence_fails_closed(self):
        self.assertEqual(assess({})['decision'], 'NO-GO')
        with self.assertRaises(ValueError): require_go({})

    def test_each_numeric_shortfall_individually_blocks_launch(self):
        for key, minimum in MINIMUMS.items():
            with self.subTest(key=key):
                report = self.complete_fixture()
                report[key] = minimum - 1
                with self.assertRaises(ValueError): require_go(report)

    def test_every_required_proof_individually_blocks_launch(self):
        for key in REQUIRED_PROOFS:
            with self.subTest(key=key):
                report = self.complete_fixture()
                report.pop(key)
                with self.assertRaises(ValueError): require_go(report)

    def test_blind_without_positive_action_cannot_pass(self):
        report = self.complete_fixture()
        report['blind_positive_lac_actions'] = 0
        with self.assertRaises(ValueError): require_go(report)

    def test_boolean_counts_and_truthy_strings_not_proof(self):
        report = self.complete_fixture()
        report['blind_positive_lac_actions'] = True
        report['actual_trainer_resume_proven'] = 'yes'
        self.assertEqual(len(assess(report)['blockers']), 2)

    def test_complete_audited_report_meets_checklist_not_model_quality(self):
        self.assertEqual(require_go(self.complete_fixture())['decision'], 'GO')


if __name__ == '__main__':
    unittest.main()
