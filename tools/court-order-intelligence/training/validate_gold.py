"""Validate evidence/targets against independent review, not model plausibility."""
import argparse
import json
import re
import hashlib
import jsonschema

from annotations.seed_review import EXAMPLES, SOURCES, PASSAGES
from foundation import compile_example, evidence, reviewed_examples
from schema.contracts import EXAMPLE, TASKS, ANCHOR_SCHEMA, ANSWER_SCHEMA
from redact_public_dataset import require_minimized


def verify_download_version(version, binary, native_pages):
    """Offline re-audit gate: different bytes are NOT the audited version.

    Matching text with a changed binary hash does not waive this check. A reviewer
    must create a new source version and reconcile relevant passages explicitly.
    """
    source = SOURCES[version]
    if hashlib.sha256(binary).hexdigest() != source["sha256"]:
        raise ValueError("Source SHA mismatch: quarantine new version; never relabel old text")
    for pid, passage in PASSAGES.items():
        key, page, text, *_ = passage
        if key == version:
            page_text = re.sub(r"\s+", " ", native_pages.get(page, "")).strip()
            if text not in page_text:
                raise ValueError("Exact bounded passage not found on its audited page")
    return True


def validate_example(example):
    jsonschema.validate(example, EXAMPLE)
    if example["state"] != "VERIFIED_GOLD":
        raise ValueError("Only explicitly VERIFIED_GOLD annotations may be exported")
    annotation = next((a for a in EXAMPLES if a[0] == example["id"]), None)
    if annotation is None:
        raise ValueError("No independent reviewed target; inference output is not gold")
    expected = compile_example(annotation)
    if expected["state"] != "VERIFIED_GOLD":
        raise ValueError("Explicit review ledger has not approved this example")
    for p in example["provenance"]:
        if p["matter_id"] != example["matter_id"]:
            raise ValueError("Cross-case/connected-case evidence mixing")
        if p != evidence(p["passage_id"]):
            raise ValueError("Source version/SHA/URL/date/page/exact-text mismatch")
        if not 1 <= p["page"] <= SOURCES[p["source_version_id"]]["page_count"]:
            raise ValueError("Page outside source version")
    contract = ANCHOR_SCHEMA if TASKS[example["task"]] == "anchors" else ANSWER_SCHEMA
    jsonschema.validate(example["target"], contract)
    # Never guess legal truth with regex. This equality binds labels, role,
    # actor context, compliance, dates and ID-only answer selection to the
    # independently authored reviewed decisions. Changing review requires a
    # new audited annotation version, not an export-time inference shortcut.
    if example != expected:
        raise ValueError("Input/target/outcome differs from independent reviewed annotation")
    for pid in annotation[3]:
        _, _, _, role, _, scope, actor = PASSAGES[pid]
        if scope == "Uncertain":
            raise ValueError("Uncertain proposition cannot be positive gold")
        if example["task"] == "office_action_detection" and (role != "COURT_DIRECTION" or scope != "Current" or actor != "LAC"):
            raise ValueError("Office action actor/operative attribution unsupported")
        if example["task"] == "compliance_state" and role != "RECORDED_COMPLIANCE":
            raise ValueError("An instruction is not confirmed completion")
    require_minimized(example)
    return example


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("jsonl", nargs="?")
    args = parser.parse_args()
    examples = reviewed_examples() if not args.jsonl else [json.loads(line) for line in open(args.jsonl, encoding="utf-8") if line.strip()]
    for example in examples:
        validate_example(example)
    print(f"Validated {len(examples)} independently reviewed examples")


if __name__ == "__main__":
    main()
