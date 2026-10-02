import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import jsonschema
from annotations.seed_review import MATTERS, PASSAGES, SOURCES
from foundation import ROOT, reviewed_examples
from validate_gold import validate_example, verify_download_version
from export_gold import export
from leakage import groups, near_duplicate, require_one_split
from build_splits import build
from redact_public_dataset import violations
from schema.contracts import ANCHOR_SCHEMA, ANSWER_SCHEMA, TASKS
from anchors import expand


class GoldTests(unittest.TestCase):
    def setUp(self):
        self.examples = {e["id"]: e for e in reviewed_examples()}
        self.sample = copy.deepcopy(self.examples["jul-extract"])

    def rejects(self, example):
        with self.assertRaises((ValueError, jsonschema.ValidationError, KeyError)):
            validate_example(example)

    def test_all_reviewed_examples_and_eight_tasks(self):
        for example in self.examples.values():
            validate_example(example)
        self.assertEqual({e["task"] for e in self.examples.values()}, set(TASKS))

    def test_anchor_targets_are_accepted_by_frozen_runtime_expansion(self):
        for e in self.examples.values():
            if TASKS[e["task"]] != "anchors":
                continue
            pages = {}
            for p in e["provenance"]:
                pages[p["page"]] = pages.get(p["page"], "") + " " + p["text"]
            with self.subTest(example=e["id"]):
                expanded = expand(e["target"], e["input"]["anchors"], pages)
                self.assertTrue(expanded["facts"])

    def test_missing_sha_url_page_or_empty_text_rejected(self):
        for field in ("sha256", "url", "page", "text"):
            with self.subTest(field=field):
                e = copy.deepcopy(self.sample)
                e["provenance"][0].pop(field)
                self.rejects(e)
        self.sample["provenance"][0]["text"] = ""
        self.rejects(self.sample)

    def test_sha_url_page_date_text_or_version_substitution_rejected(self):
        changes = {"sha256": "a" * 64, "url": "https://delhihighcourt.nic.in/wrong.pdf",
                   "page": 2, "order_date": "2027-01-01", "text": "Unsupported source fact.",
                   "source_version_id": "14604-may", "text_sha256": "b" * 64}
        for field, value in changes.items():
            with self.subTest(field=field):
                e = copy.deepcopy(self.sample)
                e["provenance"][0][field] = value
                self.rejects(e)

    def test_every_nonverified_state_and_unknown_artifact_rejected(self):
        for state in ("QUARANTINED", "UNREVIEWED", "REJECTED"):
            e = copy.deepcopy(self.sample)
            e["state"] = state
            self.rejects(e)
        self.sample["id"] = "model-output-passed-tests"
        self.rejects(self.sample)

    def test_absent_fact_and_arbitrary_prose_rejected(self):
        self.sample["target"]["facts"][0]["anchorId"] = 999
        self.rejects(self.sample)
        e = copy.deepcopy(self.examples["jul-date-en"])
        e["target"] = {"claims": [{"factId": 0, "text": "LAC paid compensation."}]}
        self.rejects(e)

    def test_party_submission_cannot_become_court_fact(self):
        e = copy.deepcopy(self.examples["jan-attribute"])
        e["target"]["facts"][0]["category"] = "COURT_FINDING"
        self.rejects(e)

    def test_unknown_role_uncertain_attribution_and_quoted_promotion_rejected(self):
        for field, value in (("category", "GUESS"), ("scope", "Uncertain"), ("scope", "Quoted")):
            e = copy.deepcopy(self.sample)
            e["target"]["facts"][0][field] = value
            self.rejects(e)
        # The runtime itself rejects a source-known quotation promoted Current.
        a = copy.deepcopy(self.sample["input"]["anchors"])
        a[0]["quoted"] = True
        with self.assertRaises(ValueError):
            expand(self.sample["target"], a, {1: " ".join(x["text"] for x in a)})

    def test_guessed_actor_completion_or_hearing_rejected(self):
        e = copy.deepcopy(self.examples["940-no-action"])
        e["target"]["claims"] = [{"factId": 0}]
        self.rejects(e)  # DDA's direction is not LAC's action.
        e = copy.deepcopy(self.examples["jan-not-complete"])
        e["target"]["claims"] = [{"factId": 0}]
        self.rejects(e)  # Direction is not completed compliance.
        e = copy.deepcopy(self.examples["jul-date-en"])
        e["input"]["availableEvidence"][2]["text"] = "List on 02.10.2026."
        self.rejects(e)

    def test_cross_case_and_connected_case_mixing_rejected(self):
        self.sample["provenance"][0] = copy.deepcopy(self.examples["940-extract"]["provenance"][0])
        self.rejects(self.sample)

    def test_qualifier_and_source_numbering_retained_exactly(self):
        e = self.examples["jan-action"]
        self.assertIn("preferably within", e["provenance"][0]["text"])
        self.assertTrue(self.examples["940-digest"]["provenance"][0]["text"].startswith("2. "))
        self.assertNotIn("deadline", e["target"])

    def test_query_variants_identical_evidence_target(self):
        base = self.examples["jul-date-en"]
        for key in ("jul-date-hi", "jul-date-hinglish", "jul-date-roman"):
            self.assertEqual(self.examples[key]["target"], base["target"])
            self.assertEqual(self.examples[key]["provenance"], base["provenance"])

    def test_office_inventory_never_exported(self):
        self.assertTrue(all(e["state"] == "VERIFIED_GOLD" for e in self.examples.values()))
        self.assertNotIn("workbook", json.dumps(list(self.examples.values())).lower())

    def test_privacy_gate_and_training_target_strictness(self):
        for text in ("email x@example.org", "phone 9876543210", "R/o Some Road", "Aadhaar number"):
            self.assertTrue(violations(text))
        e = copy.deepcopy(self.examples["jul-date-en"])
        e["input"]["question"] += " x@example.org"
        self.rejects(e)

    def test_same_text_different_download_hash_is_not_same_version(self):
        with self.assertRaisesRegex(ValueError, "SHA mismatch"):
            verify_download_version("14604-july", b"different PDF version", {1: self.sample["provenance"][0]["text"]})

    def test_source_verification_checks_exact_page_even_with_matching_hash(self):
        binary = b"synthetic offline fixture, never court evidence"
        source = {**SOURCES["14604-july"], "sha256": hashlib.sha256(binary).hexdigest()}
        pages = {1: " ".join(p[2] for p in PASSAGES.values() if p[0] == "14604-july")}
        with patch.dict(SOURCES, {"14604-july": source}):
            self.assertTrue(verify_download_version("14604-july", binary, pages))
            with self.assertRaisesRegex(ValueError, "audited page"):
                verify_download_version("14604-july", binary, {2: pages[1]})

    def test_deterministic_export_and_honestly_empty_protected_sets(self):
        with tempfile.TemporaryDirectory() as temp:
            out = export(Path(temp))
            first = {p.name: p.read_bytes() for p in out.iterdir()}
            export(out)
            self.assertEqual(first, {p.name: p.read_bytes() for p in out.iterdir()})
            for split in ("train", "validation", "blind"):
                self.assertEqual((out / f"{split}.jsonl").read_bytes(), b"")
            manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
            for name, sha in manifest["files"].items():
                self.assertEqual(hashlib.sha256((out / name).read_bytes()).hexdigest(), sha)


class SplitTests(unittest.TestCase):
    def setUp(self):
        self.examples = reviewed_examples()
        self.inventory = json.loads((ROOT / "annotations/split_inventory.json").read_text(encoding="utf-8"))

    def test_one_matter_and_same_source_always_one_group(self):
        mapping = groups(self.examples, MATTERS)
        for matter in MATTERS:
            self.assertEqual(len({mapping[e["id"]] for e in self.examples if e["matter_id"] == matter}), 1)

    def test_connected_and_common_judgment_and_same_sha_union(self):
        a, b = copy.deepcopy(self.examples[:2])
        a["id"], b["id"] = "a", "b"
        a["matter_id"], b["matter_id"] = "A", "B"
        a["provenance"] = [{"sha256": "a" * 64, "text": "Unique alpha text"}]
        b["provenance"] = [{"sha256": "b" * 64, "text": "Unique beta text"}]
        for metadata in ({"A": {"connections": ["B"]}, "B": {}},
                         {"A": {"common_judgments": ["J"]}, "B": {"common_judgments": ["J"]}}):
            self.assertEqual(len(set(groups([a, b], metadata).values())), 1)
        b["provenance"][0]["sha256"] = "a" * 64
        self.assertEqual(len(set(groups([a, b], {"A": {}, "B": {}}).values())), 1)

    def test_exact_and_near_duplicate_passages(self):
        text = "The Court directs the respondent to file the complete detailed status report within four weeks with advance copies to all parties."
        self.assertTrue(near_duplicate(text, text.upper()))
        self.assertTrue(near_duplicate(text, text.replace("four", "six")))
        self.assertFalse(near_duplicate(text, "The petition is dismissed."))
        a, b = copy.deepcopy(self.examples[:2])
        a["id"], b["id"] = "A", "B"
        a["matter_id"], b["matter_id"] = "A", "B"
        a["provenance"] = [{"sha256": "a" * 64, "text": text}]
        b["provenance"] = [{"sha256": "b" * 64, "text": text.replace("four", "six")}]
        self.assertEqual(len(set(groups([a, b], {"A": {}, "B": {}}).values())), 1)

    def test_group_cannot_cross_any_split(self):
        with self.assertRaises(ValueError):
            require_one_split({"a": "G", "b": "G"}, {"a": "train", "b": "blind"})

    def test_development_not_pristine_and_inspected_blind_rejected(self):
        self.inventory["matters"]["W.P.(C) 940/2015"]["split"] = "blind"
        with self.assertRaises(ValueError):
            build(self.examples, MATTERS, self.inventory)
        matters = copy.deepcopy(MATTERS)
        matters["W.P.(C) 940/2015"]["classification"] = "UNTOUCHED"
        record = self.inventory["matters"]["W.P.(C) 940/2015"]
        record.update(frozen=True, inspected_after_freeze=True)
        with self.assertRaises(ValueError):
            build(self.examples, matters, self.inventory)

    def test_frozen_assignment_cannot_silently_move(self):
        matter = "W.P.(C) 940/2015"
        matters = copy.deepcopy(MATTERS)
        matters[matter]["classification"] = "UNTOUCHED"
        self.inventory["matters"][matter].update(split="blind", frozen=True)
        prior = build(self.examples, matters, self.inventory)
        self.inventory["matters"][matter]["split"] = "train"
        with self.assertRaises(ValueError):
            build(self.examples, matters, self.inventory, prior)


if __name__ == "__main__":
    unittest.main()
