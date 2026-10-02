"""Deterministic serialization of explicit reviewed annotations. No inference/IO network."""
import hashlib
import json
from pathlib import Path

from annotations.seed_review import SOURCES, PASSAGES, EXAMPLES, REVIEW, VERIFIED_IDS
from schema.contracts import TASKS
from anchors import ACTORS, SUBMISSION_ROLES

ROOT = Path(__file__).resolve().parent


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def evidence(passage_id):
    version, page, text, *_ = PASSAGES[passage_id]
    source = SOURCES[version]
    return {"passage_id": passage_id, "source_version_id": version,
            "matter_id": source["matter_id"], "sha256": source["sha256"],
            "url": source["url"], "order_date": source["order_date"], "page": page,
            "text": text, "text_sha256": digest(text)}


def compile_example(annotation):
    key, task, supplied, selected, question, language, outcome = annotation
    bound = [evidence(pid) for pid in supplied]
    if not bound or len({p["matter_id"] for p in bound}) != 1:
        raise ValueError("Missing evidence or connected-case mixing")
    if not set(selected).issubset(supplied):
        raise ValueError("Target cites an absent passage")
    if TASKS[task] == "anchors":
        if len({p["source_version_id"] for p in bound}) != 1:
            raise ValueError("Extraction chunk must belong to one exact order version")
        anchors = []
        for index, pid in enumerate(supplied):
            _, page, text, role, _, scope, _ = PASSAGES[pid]
            anchors.append({"anchorId": index, "page": page, "text": text,
                            "actors": list(dict.fromkeys(m.group() for m in ACTORS.finditer(text))),
                            "quoted": scope == "Quoted",
                            "speechRole": role if role in SUBMISSION_ROLES else None,
                            "precedingContext": " ".join(PASSAGES[x][2] for x in supplied[:index])[-180:],
                            "atPageStart": False, "atPageEnd": False})
        input_value = {"caseNumber": bound[0]["matter_id"], "documentOrderDate": bound[0]["order_date"],
                       "sourceRoleContext": "", "anchors": anchors}
        target = {"facts": [{"anchorId": supplied.index(pid), "category": PASSAGES[pid][3],
                              "field": PASSAGES[pid][4], "scope": PASSAGES[pid][5]}
                             for pid in selected], "needsReview": False}
    else:
        entries = [{"factId": index, "text": p["text"], "category": PASSAGES[pid][3],
                    "scope": PASSAGES[pid][5], "source": {"orderDate": p["order_date"],
                    "page": p["page"], "evidence": p["text"], "officialUrl": p["url"]}}
                   for index, (pid, p) in enumerate(zip(supplied, bound))]
        input_value = {"currentCase": bound[0]["matter_id"], "question": question, "availableEvidence": entries}
        target = {"claims": [{"factId": supplied.index(pid)} for pid in selected]}
    return {"id": key, "task": task, "matter_id": bound[0]["matter_id"],
            "state": "VERIFIED_GOLD" if key in VERIFIED_IDS else "UNREVIEWED",
            "language": language, "review": dict(REVIEW), "outcome": outcome,
            "provenance": bound, "input": input_value, "target": target}


def reviewed_examples():
    if len({a[0] for a in EXAMPLES}) != len(EXAMPLES):
        raise ValueError("Duplicate annotation ID")
    return [compile_example(a) for a in sorted(EXAMPLES, key=lambda a: a[0])]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n", encoding="utf-8", newline="\n")


def write_jsonl(path, values):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(canonical(v) + "\n" for v in values), encoding="utf-8", newline="\n")
