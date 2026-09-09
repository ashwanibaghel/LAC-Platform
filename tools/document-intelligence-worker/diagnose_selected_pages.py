"""Run a local-only, temporary selected-page Court-cost comparison."""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path


def run(worker: Path, pdf: Path, pages: str, disable_court: bool, output: Path) -> dict:
    command = [sys.executable, str(worker), "--input", str(output.with_suffix(".input.json")), "--output", str(output), "--pages", pages]
    if disable_court:
        command.append("--disable-court")
    output.with_suffix(".input.json").write_text(json.dumps({"contractVersion": 1, "documentId": str(uuid.uuid4()), "filePath": str(pdf)}), encoding="utf-8")
    completed = subprocess.run(command, capture_output=True, text=True, check=False)
    if completed.returncode:
        # OCR libraries write startup diagnostics first; the actionable worker
        # exception is at the end. Keep it local and bounded for diagnostics.
        details = (completed.stderr or completed.stdout).strip()
        raise RuntimeError(f"worker failed: {details[-900:]}")
    payload = json.loads(output.read_text(encoding="utf-8"))
    metrics = payload["metrics"]
    candidates = payload.get("candidates", [])
    type_counts: dict[str, int] = {}
    for candidate in candidates:
        type_name = str(candidate.get("candidateType", "Unknown"))
        type_counts[type_name] = type_counts.get(type_name, 0) + 1
    court_candidates = [candidate for candidate in candidates if candidate.get("candidateType") == "CourtCase"]
    aggregate = {
        "candidateCount": len(candidates),
        "candidateTypes": type_counts,
        "courtCandidatesWithSourceRegion": sum(1 for candidate in court_candidates if candidate.get("sourceRegion")),
    }
    return {key: metrics.get(key) for key in ("runtimeSeconds", "pageOcrCalls", "tableTransformerCalls", "cellOcrCalls", "cellOcrCacheHits", "courtTableRowsInspected", "headerFallbackInvocations", "geometryTables", "tablesWithoutGridRows", "tablesWithGridRows", "tablesUnclassified", "courtHeaderSignals", "khasraHeaderSignals", "stageSeconds")} | aggregate


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pdf", type=Path, required=True)
    parser.add_argument("--pages", required=True)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    if not args.pdf.is_file():
        raise SystemExit("local PDF not found")
    worker = Path(__file__).with_name("worker.py")
    with tempfile.TemporaryDirectory(prefix="lac-court-profile-") as directory:
        root = Path(directory)
        disabled = run(worker, args.pdf, args.pages, True, root / "disabled.json")
        enabled = run(worker, args.pdf, args.pages, False, root / "enabled.json")
    # Aggregate metrics only. OCR text, page images and worker output remain temporary.
    report = {"pages": args.pages, "courtDisabled": disabled, "courtEnabled": enabled,
              "incrementalCourtSeconds": round(enabled["runtimeSeconds"] - disabled["runtimeSeconds"], 3)}
    if args.report:
        args.report.write_text(json.dumps(report), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
