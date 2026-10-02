"""Public-only manifest allowlist; no private inventory or whole-page upload."""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from foundation import canonical, write_json, write_jsonl
from annotations import pilot_review as corpus
from validate_gold import validate_example
from prepare_smoke_dataset import QA_INSTRUCTIONS
from anchors import INSTRUCTIONS
from schema.contracts import TASKS


def prepare(output):
    if output.exists():
        raise ValueError("Fresh bundle path required")
    source = HERE.parent / "pilot-v1"
    metadata = json.loads((source / "manifest.json").read_text())
    for name, sha in metadata["files"].items():
        if hashlib.sha256((source / name).read_bytes()).hexdigest() != sha:
            raise ValueError("Reviewed data manifest mismatch")
    if hashlib.sha256(Path(corpus.__file__).read_bytes()).hexdigest() != metadata["annotations_sha256"]:
        raise ValueError("Independent annotation review changed")
    if hashlib.sha256(corpus.LEDGER.read_bytes()).hexdigest() != metadata["source_ledger_sha256"]:
        raise ValueError("Audited source ledger changed")
    output.mkdir(parents=True)
    counts = {}
    for name in ("train", "validation", "blind"):
        records = []
        for line in (source / (name + ".jsonl")).read_text(encoding="utf-8").splitlines():
            example = validate_example(json.loads(line), corpus)
            kind = TASKS[example["task"]]
            records.append({"id": example["id"], "matter_id": example["matter_id"], "task": example["task"],
                "language": example["language"], "outcome": example["outcome"],
                "contract": kind, "input": example["input"], "expected": example["target"],
                "messages": [{"role": "system", "content": INSTRUCTIONS if kind == "anchors" else QA_INSTRUCTIONS},
                             {"role": "user", "content": canonical(example["input"])},
                             {"role": "assistant", "content": canonical(example["target"])}]})
        write_jsonl(output / (name + ".jsonl"), records)
        counts[name] = len(records)
    for name in ("train_smoke.py", "smoke_contract.py", "reload_smoke.py", "pilot_contract.py",
                 "pilot_train.py", "requirements.txt", "training_config_pilot.json"):
        shutil.copyfile(HERE / name, output / name)
    shutil.copyfile(source / "target.schemas.json", output / "target.schemas.json")
    shutil.copyfile(source / "splits.json", output / "splits.json")
    runtime = output / "runtime"
    runtime.mkdir()
    for name in ("anchors.py", "semantics.py"):
        shutil.copyfile(HERE.parent.parent / name, runtime / name)
    files = {str(p.relative_to(output)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in output.rglob("*") if p.is_file()}
    write_json(output / "dataset-manifest.json", {"purpose": ["PILOT_V1", "BOUNDED_EVALUATION", "NO_DEPLOYMENT"],
        "private": True, "source_sha": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=HERE, text=True).strip(),
        "model": metadata["model_id"], "revision": metadata["revision"], "counts": counts, "files": files,
        "private_workbook_included": False, "weights_included": False, "pdfs_included": False})
    print(json.dumps({"bundle": str(output), "counts": counts}))


if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--output", required=True, type=Path)
    prepare(cli.parse_args().output.resolve())
