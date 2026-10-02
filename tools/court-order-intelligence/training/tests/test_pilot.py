import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent))
from annotations import pilot_review as corpus
from foundation import compile_example
from validate_gold import validate_example
from build_splits import build
from acquire_pilot_sources import acquire
from unittest.mock import patch


class PilotSafety(unittest.TestCase):
    def test_every_example_passes_independent_review_and_runtime_schema(self):
        for annotation in corpus.EXAMPLES:
            example = compile_example(annotation, corpus)
            if example["state"] == "VERIFIED_GOLD":
                validate_example(example, corpus)
            else:
                with self.assertRaises(ValueError):
                    validate_example(example, corpus)

    def test_anchor_gold_passes_unchanged_runtime_expansion(self):
        from anchors import expand
        for annotation in corpus.EXAMPLES:
            example = compile_example(annotation, corpus)
            if "anchors" not in example["input"] or example["state"] != "VERIFIED_GOLD":
                continue
            pages = {}
            for anchor in example["input"]["anchors"]:
                pages[anchor["page"]] = pages.get(anchor["page"], "") + " " + anchor["text"]
            with self.subTest(example=example["id"]):
                expand(example["target"], example["input"]["anchors"], pages)

    def test_mutated_source_and_target_rejected(self):
        original = compile_example(next(a for a in corpus.EXAMPLES if a[0] == "lpa543-2026-extract"), corpus)
        for key, value in (("sha256", "0" * 64), ("page", 999), ("matter_id", "Other case"), ("order_date", "1990-01-01")):
            item = copy.deepcopy(original)
            item["provenance"][0][key] = value
            with self.assertRaises(ValueError):
                validate_example(item, corpus)
        item = copy.deepcopy(original)
        item["target"]["facts"][0]["category"] = "COURT_FINDING"
        with self.assertRaises(ValueError):
            validate_example(item, corpus)

    def test_quoted_direction_not_office_action(self):
        row = next(a for a in corpus.EXAMPLES if a[0] == "wpc8404-2024-action")
        self.assertNotIn("wpc8404-2024-p1", row[3])
        self.assertEqual(row[3], ["wpc8404-2024-p3"])

    def test_party_filing_assertion_not_recorded_compliance(self):
        row = next(a for a in corpus.EXAMPLES if a[0] == "cs23-2025-compliance")
        self.assertEqual(row[3], [])
        self.assertEqual(row[-1], "NOT_CONFIRMED_COMPLETE")

    def test_frozen_groups_and_development_exclusion(self):
        splits = json.loads((ROOT / "pilot-v1/splits.json").read_text())
        examples = [compile_example(a, corpus) for a in corpus.EXAMPLES]
        build(examples, corpus.MATTERS, splits["matter_inventory"], splits)
        self.assertEqual(len(set(splits["leakage_groups"].values())), 15)
        self.assertFalse(set(corpus.MATTERS) & set(splits["matter_inventory"]["old_development_only"]))
        self.assertEqual(sum(m["split"] == "blind" for m in splits["matter_inventory"]["matters"].values()), 2)

    def test_temporary_pdf_cleanup_on_parser_failure(self):
        observed = []
        def fake_download(url, directory):
            path = Path(directory) / "order.pdf"
            path.write_bytes(b"%PDF-fake")
            observed.append(path)
            return path, "0" * 64
        with patch("acquire_pilot_sources.download", side_effect=fake_download), patch("acquire_pilot_sources.native_pages", side_effect=ValueError("No text")):
            with self.assertRaises(ValueError):
                acquire("case", "url")
        self.assertFalse(observed[0].exists())

    def test_frozen_officer_questions_are_public_bounded_and_not_training(self):
        from annotations.pilot_officer_review import records
        from redact_public_dataset import violations
        from jsonschema import validate
        from schema.contracts import ANSWER_SCHEMA
        rows = records()
        self.assertEqual(len(rows), 26)
        for record in rows:
            self.assertFalse(record["training_eligible"])
            self.assertTrue(record["frozen_before_model_output_inspection"])
            validate(record["expected"], ANSWER_SCHEMA)
            entries = record["input"]["availableEvidence"]
            self.assertLessEqual(len(entries), 8)
            for entry in entries:
                self.assertFalse(violations(entry["text"]))
                self.assertEqual(entry["text"], entry["source"]["evidence"])
            for claim in record["expected"]["claims"]:
                self.assertLess(claim["factId"], len(entries))


if __name__ == "__main__":
    unittest.main()
