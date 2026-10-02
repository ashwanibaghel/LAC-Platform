import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
from pilot_contract import verify_config
from pilot_train import score, aggregate
from smoke_contract import MODEL, REVISION


class PilotContractTests(unittest.TestCase):
    def test_pinned_bounded_config(self):
        config = json.loads((HERE / "training_config_pilot.json").read_text())
        verify_config(config)
        for key, value in (("revision", "main"), ("max_steps", 1000), ("batch_size", 2), ("learning_rate", 1.0)):
            changed = dict(config, **{key: value})
            with self.assertRaises(ValueError):
                verify_config(changed)

    def test_wrong_qualified_role_is_not_correct(self):
        expected = {"facts": [{"anchorId": 0, "category": "PETITIONER_SUBMISSION", "field": "context", "scope": "Current"}], "needsReview": False}
        record = {"id": "x", "task": "attribution_classification", "matter_id": "case", "language": "English", "contract": "anchors", "expected": expected}
        predicted = copy.deepcopy(expected)
        predicted["facts"][0]["category"] = "COURT_FINDING"
        result = score(record, predicted, True)
        self.assertFalse(result["exact_target"])
        self.assertEqual(result["semantic_role_correct"], [0, 1])
        self.assertEqual(result["wrong_role_or_scope"], 1)

    def test_negative_hallucination_and_parser_failure_not_success(self):
        record = {"id": "x", "task": "compliance_state", "matter_id": "case", "language": "English", "contract": "claims", "expected": {"claims": []}}
        result = score(record, {"claims": [{"factId": 0}]}, True)
        self.assertEqual(result["unsupported_negative_answer"], 1)
        self.assertFalse(result["exact_target"])
        self.assertFalse(score(record, {"claims": []}, False)["exact_target"])

    def test_same_safety_layer_and_no_training_on_protected_splits(self):
        text = (HERE / "pilot_train.py").read_text()
        self.assertIn('model.disable_adapter()', text)
        self.assertIn('inference_messages(record, schemas)', text)
        self.assertIn('parse_runtime_output(raw, record, schemas', text)
        self.assertNotIn('do_sample=True', text)
        contract = (HERE / "pilot_contract.py").read_text()
        self.assertIn('return sets["train"], manifest', contract)


if __name__ == "__main__":
    unittest.main()
