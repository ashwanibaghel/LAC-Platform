"""Use actual frozen product JSON contracts, never unconstrained legal prose."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from anchors import ANCHOR_SCHEMA
from questions import ANSWER_SCHEMA

TASKS = {
    "semantic_proposition_extraction": "anchors",
    "attribution_classification": "anchors",
    "important_fact_selection": "claims",
    "office_action_detection": "claims",
    "compliance_state": "claims",
    "date_specific_retrieval_or_QA": "claims",
    "order_digest": "claims",
    "multi_order_current_position": "claims"}
STATES = ["VERIFIED_GOLD", "QUARANTINED", "UNREVIEWED", "REJECTED"]


def strict(properties):
    return {"type": "object", "properties": properties,
            "required": list(properties), "additionalProperties": False}


TEXT = {"type": "string", "minLength": 1}
EVIDENCE = strict({
    "passage_id": TEXT, "source_version_id": TEXT, "matter_id": TEXT,
    "sha256": {"type": "string", "pattern": "^[a-f0-9]{64}$"},
    "url": {"type": "string", "pattern": "^https://delhihighcourt\\.nic\\.in/"},
    "order_date": {"type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}$"},
    "page": {"type": "integer", "minimum": 1},
    "text": {"type": "string", "minLength": 8, "maxLength": 900},
    "text_sha256": {"type": "string", "pattern": "^[a-f0-9]{64}$"}})
EXAMPLE = strict({
    "id": TEXT, "task": {"enum": list(TASKS)}, "matter_id": TEXT,
    "state": {"enum": STATES}, "language": {"enum": ["English", "Hindi", "Hinglish", "RomanHindi"]},
    "review": strict({"reviewer": TEXT, "method": TEXT, "reviewed_on": TEXT, "annotation_version": TEXT}),
    "outcome": {"enum": ["SUPPORTED", "NONE", "INSUFFICIENT_EVIDENCE", "NOT_CONFIRMED_COMPLETE"]},
    "provenance": {"type": "array", "minItems": 1, "maxItems": 8, "items": EVIDENCE},
    "input": {"type": "object"}, "target": {"oneOf": [ANCHOR_SCHEMA, ANSWER_SCHEMA]}})
