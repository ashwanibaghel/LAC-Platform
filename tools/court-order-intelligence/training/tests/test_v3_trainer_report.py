import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class TrainerReportTests(unittest.TestCase):
    def test_actual_pinned_cpu_trainer_proof_not_quality_evidence(self):
        proof = json.loads((ROOT / 'kaggle/reports/v3-trainer-integration-proof-v3.2.json').read_text())
        self.assertEqual(proof['result'], 'PASS')
        self.assertEqual(proof['transformers'], '4.56.2')
        self.assertEqual(proof['accelerate'], '1.10.1')
        self.assertEqual(proof['consumed_microbatches'], 320)
        self.assertTrue(proof['continuous_resume_ids_exact_match'])
        self.assertEqual(proof['simulated_applied_updates'], 79)
        self.assertEqual(proof['simulated_skips'], 1)
        self.assertTrue(proof['software_fixture_only'])
        self.assertFalse(proof['training_gold_used'])
        self.assertFalse(proof['real_cuda_gradscaler_proven'])
        self.assertFalse(proof['network_used'])
        self.assertEqual(proof['curriculum_state']['logical_sample_index'], 320)
        self.assertEqual(len(proof['exposure']['leakage_group_exposures']), 3)
        self.assertEqual(proof['exposure']['consecutive_same_matter'], 0)

    def test_trainer_wiring_is_not_legacy_cyclic_generator(self):
        source = (ROOT / 'v3_trainer.py').read_text()
        self.assertIn('SequentialSampler', source)
        self.assertIn('curriculum_index', source)
        self.assertIn('CurriculumCheckpoint(data)', source)
        self.assertIn("actual != self.train_dataset.state_at(step)", source)
        self.assertNotIn('yield from encoded', source)


if __name__ == '__main__':
    unittest.main()
