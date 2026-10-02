import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from v3_experiment_train import verify_experiment, PURPOSE, PENDING_PURPOSE, make_training_data
from prepare_v3_experiment import prepare_experiment


class ExperimentTests(unittest.TestCase):
    def fixture(self, root):
        (root / 'train.jsonl').write_text('{"id":"public-fixture"}\n')
        sha = hashlib.sha256((root / 'train.jsonl').read_bytes()).hexdigest()
        original = dict(files={'train.jsonl': sha})
        (root / 'preflight-manifest.json').write_text(json.dumps(original))
        proof = dict(result='PASS', resume_ids_exact=True, logical_steps=6, consumed_microbatches=24,
            full_v3_training_launched=False,
            bundle_manifest_sha256=hashlib.sha256((root / 'preflight-manifest.json').read_bytes()).hexdigest(),
            encoded_candidate_count=1, examples_dropped=0, evidence_truncated=False,
            chosen_context=3707, required_context=3707,
            exact_masked_sft_loss_proof=dict(result='PASS', loss_and_all_gradients_equivalent=True),
            memory_probes=[dict(context=3707, forward_backward=True, safe_headroom=True)])
        (root / 'gpu-preflight-proof.json').write_text(json.dumps(proof))
        manifest = dict(purpose=PURPOSE, private=True, pdfs_included=False, private_workbook_included=False,
                        weights_included=False, quality_acceptance_passed=False, candidate_count=1,
                        files={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir()})
        (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
        return proof, manifest

    def test_exact_safe_hardware_proof_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.fixture(root)
            rows, manifest, proof = verify_experiment(root)
            self.assertEqual(len(rows), 1)
            self.assertFalse(manifest['quality_acceptance_passed'])

    def test_pending_bundle_cannot_start_fit_before_real_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _, manifest = self.fixture(root)
            manifest.update(purpose=PENDING_PURPOSE, requires_gpu_proof=True)
            (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
            verify_experiment(root, pending_gate=True)
            with self.assertRaises(ValueError):
                verify_experiment(root)
        code = (Path(__file__).resolve().parents[1] / 'run_v3_experiment.py').read_text()
        self.assertLess(code.index("str(preflight / 'v3_gpu_preflight.py')"),
                        code.index("str(bundle / 'v3_experiment_train.py')"))
        self.assertIn('str(proof_dir)], env=environment, check=True)', code)
        self.assertIn('verify_experiment(bundle)', code)

    def test_failed_truncated_or_unsafe_proof_refused_even_when_checksum_updated(self):
        for change in ({'result': 'FAIL'}, {'evidence_truncated': True}, {'resume_ids_exact': False},
                       {'chosen_context': 3072}, {'memory_probes': []}):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                proof, manifest = self.fixture(root)
                proof.update(change)
                path = root / 'gpu-preflight-proof.json'
                path.write_text(json.dumps(proof))
                manifest['files'][path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
                (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
                with self.assertRaises(ValueError):
                    verify_experiment(root)

    def test_changed_gold_cannot_reuse_proof(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            _, manifest = self.fixture(root)
            (root / 'train.jsonl').write_text('{"id":"modified"}\n')
            manifest['files']['train.jsonl'] = hashlib.sha256((root / 'train.jsonl').read_bytes()).hexdigest()
            (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
            with self.assertRaises(ValueError):
                verify_experiment(root)

    def test_fail_before_packaging_failed_preflight(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            proof = root / 'proof.json'
            proof.write_text('{"result":"FAIL"}')
            with self.assertRaises(ValueError):
                prepare_experiment(root / 'new', proof, root / 'unused.json')
            self.assertFalse((root / 'new').exists())

    def test_full_fit_is_bounded_fresh_base_and_does_not_force_scaler_overflow(self):
        source = Path(__file__).resolve().parents[1]
        code = (source / 'v3_experiment_train.py').read_text()
        self.assertIn('calculate_budget(len(rows), 4, 3)', code)
        self.assertIn('consumed != list(data.ids)', code)
        self.assertIn("model = load_base()", code)
        self.assertIn("resume_from_checkpoint=str(checkpoint)", code)
        self.assertNotIn("grad.fill_", code)
        self.assertIn('TRAIN representative integration checks only', code)
        for secret in ('kaggle.json', 'KAGGLE_API_TOKEN', 'delhihighcourt.nic.in', 'MigrateAsync'):
            self.assertNotIn(secret, code)

    def test_every_original_exposed_and_actual_trainer_resume_is_exact(self):
        sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
        from prove_v3_trainer import run
        def factory(rows, encoded):
            converted = [dict(row, expected=row['target']) for row in rows]
            data = make_training_data(converted, encoded, {'trainer_steps': 80})
            self.assertEqual(set(data.ids[:len(rows)]), set(encoded))
            self.assertEqual(data.report['unique_records_exposed'], len(rows))
            self.assertEqual(data.ids, make_training_data(converted, encoded, {'trainer_steps': 80}).ids)
            return data
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / 'proof.json'
            run(report, dataset_factory=factory)
            actual = json.loads(report.read_text())
            self.assertTrue(actual['continuous_resume_ids_exact_match'])
            self.assertEqual(actual['consumed_microbatches'], 320)
            self.assertEqual(actual['simulated_applied_updates'], 79)


if __name__ == '__main__':
    unittest.main()
