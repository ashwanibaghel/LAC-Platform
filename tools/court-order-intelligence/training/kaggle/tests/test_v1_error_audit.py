import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_v1_errors import audit, verify_artifacts


class V1AuditTests(unittest.TestCase):
    def fixture(self):
        record = {'id': 'example', 'matter_id': 'case', '_split': 'blind',
                  'contract': 'claims', 'task': 'office_action_detection',
                  'expected': {'claims': [{'factId': 0}]},
                  'input': {'availableEvidence': [{'text': 'source'}]}}
        row = {'id': 'example', 'matter_id': 'case', 'split': 'blind',
               'model': 'fine_tuned_4b', 'exact_target': False,
               'raw_output': '{"claims":[]}', 'parsed_output': {'claims': []}}
        schemas = {'claims': {'type': 'object'}}
        return row, record, schemas

    def test_office_miss_and_blind_reuse_prohibited(self):
        row, record, schemas = self.fixture()
        snapshot = copy.deepcopy(record)
        report = audit([row], {'example': record}, schemas, Path('.'))
        self.assertIn('office_action_miss', report['failures'][0]['buckets'])
        self.assertFalse(report['failures'][0]['training_reuse_allowed'])
        self.assertEqual(record, snapshot)

    def test_out_of_range_id_is_parser_error_not_just_qa_miss(self):
        row, record, schemas = self.fixture()
        record['task'] = 'date_specific_retrieval_or_QA'
        row['raw_output'] = '{"claims":[{"factId":5}]}'
        row['parsed_output'] = {'claims': [{'factId': 5}]}
        failure = audit([row], {'example': record}, schemas, Path('.'))['failures'][0]
        self.assertEqual(failure['parser_diagnostic'], 'Unretrieved fact ID')
        self.assertIn('date_qa', failure['buckets'])

    def test_split_mismatch_rejected(self):
        row, record, schemas = self.fixture()
        row['split'] = 'validation'
        with self.assertRaises(ValueError):
            audit([row], {'example': record}, schemas, Path('.'))

    def test_duplicate_output_rejected(self):
        row, record, schemas = self.fixture()
        with self.assertRaises(ValueError):
            audit([row, row], {'example': record}, schemas, Path('.'))

    def test_incomplete_freeze_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'artifact-checksums.json').write_text(json.dumps({'adapter/file': 'missing'}))
            with self.assertRaises(ValueError):
                verify_artifacts(path)

    def test_artifact_path_escape_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / 'artifact-checksums.json').write_text(json.dumps({'../secret': 'missing'}))
            with self.assertRaises(ValueError):
                verify_artifacts(path)


if __name__ == '__main__':
    unittest.main()
