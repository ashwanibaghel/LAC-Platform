import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from pilot_v2_contract import verify_config, verify_bundle, PURPOSE
from pilot_v2_train import detailed_score, metrics
from reload_smoke import inference_messages


class PilotV2Safety(unittest.TestCase):
    def config(self):
        config = json.loads((HERE / 'training_config_pilot.json').read_text())
        config.update({'purpose': PURPOSE, 'oom_sequence_lengths': []})
        return config

    def test_bounded_identical_architecture(self):
        self.assertEqual(verify_config(self.config())['max_steps'], 80)

    def test_model_change_rejected(self):
        config = self.config()
        config['revision'] = 'main'
        with self.assertRaises(ValueError):
            verify_config(config)

    def test_shorter_context_data_drop_rejected(self):
        config = self.config()
        config['oom_sequence_lengths'] = [1024, 512]
        with self.assertRaises(ValueError):
            verify_config(config)

    def test_unbounded_or_repeated_fit_rejected(self):
        config = self.config()
        config['max_steps'] = 120
        with self.assertRaises(ValueError):
            verify_config(config)

    def test_expected_answer_not_in_inference(self):
        row = {'messages': [{'role': 'system', 'content': 'rules'}, {'role': 'user', 'content': 'evidence'},
                            {'role': 'assistant', 'content': 'SECRET_TARGET'}], 'contract': 'claims'}
        self.assertNotIn('SECRET_TARGET', str(inference_messages(row, {'claims': {}})))

    def test_role_field_scope_errors_are_separate(self):
        row = {'id': 'a', 'task': 'attribution_classification', 'matter_id': 'm', 'language': 'English',
               'contract': 'anchors', 'expected': {'facts': [{'anchorId': 0, 'category': 'PETITIONER_SUBMISSION', 'field': 'context', 'scope': 'Historical'}]}}
        got = {'facts': [{'anchorId': 0, 'category': 'COURT_FINDING', 'field': 'possession', 'scope': 'Current'}]}
        result = detailed_score(row, got, True)
        self.assertEqual([result[k] for k in ('wrong_category', 'wrong_field', 'wrong_scope', 'submission_as_court')], [1, 1, 1, 1])

    def test_office_action_misses_retained_in_denominator(self):
        row = {'id': 'a', 'task': 'office_action_detection', 'matter_id': 'm', 'language': 'English',
               'contract': 'claims', 'expected': {'claims': [{'factId': 0}]}}
        result = detailed_score(row, {'claims': []}, True)
        self.assertEqual(result['office_action_recall'], [0, 1])
        self.assertEqual(metrics([result])['missed_office_actions'], 1)

    def test_no_unsupported_selection_reward(self):
        row = {'id': 'a', 'task': 'office_action_detection', 'matter_id': 'm', 'language': 'English',
               'contract': 'claims', 'expected': {'claims': []}}
        result = detailed_score(row, {'claims': [{'factId': 0}]}, True)
        self.assertEqual(result['unsupported_negative_answer'], 1)
        self.assertFalse(result['exact_target'])

    def test_bundle_rejects_unlisted_weight_before_gpu(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            manifest = {'purpose': PURPOSE, 'private': True, 'protected_splits_frozen': True,
                'model': self.config()['model_id'], 'revision': self.config()['revision'],
                'private_workbook_included': False, 'weights_included': False, 'pdfs_included': False, 'files': {}}
            (path / 'dataset-manifest.json').write_text(json.dumps(manifest))
            (path / 'private-workbook.xlsx').write_bytes(b'not an allowed public passage')
            with self.assertRaisesRegex(ValueError, 'unlisted'):
                verify_bundle(path)

    def test_unfrozen_bundle_cannot_train(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            (path / 'dataset-manifest.json').write_text(json.dumps({'purpose': PURPOSE, 'private': True, 'protected_splits_frozen': False}))
            with self.assertRaisesRegex(ValueError, 'frozen'):
                verify_bundle(path)


if __name__ == '__main__':
    unittest.main()
