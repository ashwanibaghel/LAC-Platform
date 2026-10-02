import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from v3_gpu_preflight import verify_bundle, PURPOSE
from run_v3_preflight import stage_bundle


class PreflightBundleTests(unittest.TestCase):
    def bundle(self, root):
        rows = [dict(id='fixture-only')]
        (root / 'train.jsonl').write_text(json.dumps(rows[0]) + '\n')
        files = {'train.jsonl': hashlib.sha256((root / 'train.jsonl').read_bytes()).hexdigest()}
        manifest = dict(purpose=PURPOSE, private=True, pdfs_included=False,
                        private_workbook_included=False, weights_included=False, candidate_count=1, files=files)
        (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
        return manifest

    def test_complete_exact_inventory_passes_without_gpu_or_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.bundle(root)
            rows, manifest = verify_bundle(root)
            self.assertEqual(len(rows), 1)
            self.assertEqual(manifest['purpose'], PURPOSE)

    def test_modified_extra_private_or_missing_records_fail_closed(self):
        for mutation in ('extra', 'changed', 'private', 'count', 'purpose'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                manifest = self.bundle(root)
                if mutation == 'extra':
                    (root / 'unlisted.pdf').write_bytes(b'not real PDF')
                elif mutation == 'changed':
                    (root / 'train.jsonl').write_text('{}')
                else:
                    manifest.update({'private_workbook_included': True} if mutation == 'private'
                                    else {'candidate_count': 2} if mutation == 'count'
                                    else {'purpose': 'FULL_V3_TRAINING'})
                    (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
                with self.assertRaises(ValueError):
                    verify_bundle(root)

    def test_escape_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = self.bundle(root)
            manifest['files']['../secret'] = 'a' * 64
            (root / 'dataset-manifest.json').write_text(json.dumps(manifest))
            with self.assertRaises(ValueError):
                verify_bundle(root)

    def test_expanded_kaggle_dataset_is_staged_and_sha_verified(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inputs = root / 'inputs'
            inputs.mkdir()
            self.bundle(inputs)
            destination = root / 'working'
            stage_bundle(inputs, destination)
            rows, _ = verify_bundle(destination)
            self.assertEqual(rows[0]['id'], 'fixture-only')

    def test_expanded_manifest_escape_rejected_before_copy(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            inputs = root / 'inputs'
            inputs.mkdir()
            manifest = self.bundle(inputs)
            manifest['files']['../secret.txt'] = 'a' * 64
            (inputs / 'dataset-manifest.json').write_text(json.dumps(manifest))
            with self.assertRaises(RuntimeError):
                stage_bundle(inputs, root / 'working')

    def test_runner_is_bounded_not_a_full_fit_or_office_action(self):
        source = Path(__file__).resolve().parents[1]
        trainer = (source / 'v3_gpu_preflight.py').read_text()
        self.assertIn('CurriculumDataset(rows, encoded, 20261003, 6, 4)', trainer)
        self.assertIn('max_steps=6', trainer)
        self.assertIn('for cap in (3072, 4096)', trainer)
        self.assertIn('consumed != list(data.ids)', trainer)
        self.assertIn('grad.fill_(float(\'inf\'))', trainer)
        for forbidden in ('kaggle.json', 'KAGGLE_API_TOKEN', 'delhihighcourt.nic.in', '/resume', 'MigrateAsync'):
            self.assertNotIn(forbidden, trainer)


if __name__ == '__main__':
    unittest.main()
