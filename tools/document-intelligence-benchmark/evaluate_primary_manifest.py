"""Compare a fresh worker result with manually checked source examples.

The manifest's untranscribed rows are REVIEW, not implied OCR ground truth.
This script never turns machine output into the expected source values.
"""
import argparse
import collections
import json
import re
from pathlib import Path


def compact(value):
    return "".join(str(value or "").lower().split())


def raw_area(value):
    # Compare only spacing and repeated dash typography. A stray OCR dot or
    # digit remains a disagreement; this is not a canonical area parser.
    return re.sub(r"-{2,}", "-", compact(value))


def evaluate(manifest, result, row_regions=None):
    source_pages = {int(page) for page in manifest["sourcePages"]}
    candidates = [candidate for candidate in result["candidates"]
                  if candidate["candidateType"] == "AwardKhasra"]
    primary = [candidate for candidate in candidates
               if candidate["structuredPayload"].get("sourceSection") == "PRIMARY_AWARDED_LAND"]
    possession = [candidate for candidate in candidates if candidate["page"] not in source_pages]
    by_page = collections.Counter(candidate["page"] for candidate in primary)
    by_group = collections.Counter((candidate["page"], candidate["structuredPayload"]["logicalGroupId"])
                                   for candidate in primary)
    used = set()
    counts = collections.Counter()
    failures = []
    for sample in manifest["verifiedSamples"]:
        matches = [(index, candidate) for index, candidate in enumerate(primary)
                   if index not in used and candidate["page"] == sample["page"]
                   and candidate["structuredPayload"]["logicalGroupId"] == sample["group"]
                   and compact(candidate["structuredPayload"]["khasraNumber"] +
                               ("min" if candidate["structuredPayload"].get("qualifier") else "")) == compact(sample["khasra"])]
        if not matches:
            failures.append({"sample": sample, "result": "identifier missing or wrong"})
            continue
        def area(candidate, name):
            return compact(candidate["structuredPayload"][name]["normalizedSuggestion"])
        def raw(candidate, name):
            return raw_area(candidate["structuredPayload"][name]["rawOcr"])
        index, best = min(matches, key=lambda item:
                          (raw(item[1], "recordedArea") != raw_area(sample["recorded"])) +
                          (raw(item[1], "awardedArea") != raw_area(sample["awarded"])) +
                          (area(item[1], "recordedArea") != compact(sample["recorded"])) +
                          (area(item[1], "awardedArea") != compact(sample["awarded"])))
        used.add(index)
        counts["identifierExact"] += 1
        recorded = area(best, "recordedArea") == compact(sample["recorded"])
        awarded = area(best, "awardedArea") == compact(sample["awarded"])
        raw_recorded = raw(best, "recordedArea") == raw_area(sample["recorded"])
        raw_awarded = raw(best, "awardedArea") == raw_area(sample["awarded"])
        counts["recordedExact"] += recorded
        counts["awardedExact"] += awarded
        counts["fullyExact"] += recorded and awarded
        counts["recordedRawExact"] += raw_recorded
        counts["awardedRawExact"] += raw_awarded
        counts["fullyRawExact"] += raw_recorded and raw_awarded
        if not (recorded and awarded):
            failures.append({"sample": sample, "result": "area mismatch or requires normalization review", "actual": {
                "recorded": best["structuredPayload"]["recordedArea"]["normalizedSuggestion"],
                "awarded": best["structuredPayload"]["awardedArea"]["normalizedSuggestion"],
                "recordedRaw": best["structuredPayload"]["recordedArea"]["rawOcr"],
                "awardedRaw": best["structuredPayload"]["awardedArea"]["rawOcr"]}})
    identities = [(candidate["page"], candidate["structuredPayload"]["tableId"],
                   candidate["structuredPayload"]["rowId"], candidate["structuredPayload"]["logicalGroupId"])
                  for candidate in primary]
    row_alignment = {}
    if row_regions is not None:
        matched = collections.Counter()
        orphans = []
        for candidate in primary:
            page = str(candidate["page"])
            group = candidate["structuredPayload"]["logicalGroupId"]
            centers = row_regions["rowCentersByPageAndGroup"][page][group - 1]
            region = candidate["sourceRegion"]
            cy = region["y"] + region["height"] / 2
            closest = min(range(len(centers)), key=lambda index: abs(centers[index] - cy))
            if abs(centers[closest] - cy) > 20:
                orphans.append((page, group, round(cy)))
            else:
                matched[(page, group, closest)] += 1
        row_alignment = {
            "geometryMatchedSourceRows": len(matched),
            "geometryMissingRows": manifest["physicalRowsTotal"] - len(matched),
            "geometryOrphanCandidates": len(orphans),
            "geometryDuplicateCandidates": sum(count - 1 for count in matched.values()),
            "geometryOrphanExamples": orphans[:20],
        }
    return {
        "physicalSourceRows": manifest["physicalRowsTotal"],
        "physicalByPage": {page: sum(groups) for page, groups in manifest["physicalRowsByPageAndGroup"].items()},
        "primaryCandidates": len(primary), "primaryCandidatesByPage": dict(sorted(by_page.items())),
        "primaryCandidatesByPageAndGroup": {f"{page}:{group}": count for (page, group), count in sorted(by_group.items())},
        "possessionAnnexureAwardKhasra": len(possession),
        "candidateLocatorDuplicates": len(identities) - len(set(identities)),
        "verifiedSourceSamples": len(manifest["verifiedSamples"]),
        "verifiedSampleCounts": dict(counts),
        "verifiedSampleMissingOrWrongIdentifier": len(manifest["verifiedSamples"]) - counts["identifierExact"],
        "verifiedSampleFailures": failures,
        "unverifiedPopulationRows": manifest["physicalRowsTotal"] - len(manifest["verifiedSamples"]),
        **row_alignment,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("worker_output", type=Path)
    parser.add_argument("--manifest", type=Path, default=Path(__file__).parent / "fixtures" / "pochanpur_primary_land_manifest.json")
    parser.add_argument("--row-regions", type=Path, default=Path(__file__).parent / "fixtures" / "pochanpur_primary_row_regions.json")
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    result = json.loads(args.worker_output.read_text(encoding="utf-8"))
    row_regions = json.loads(args.row_regions.read_text(encoding="utf-8"))
    print(json.dumps(evaluate(manifest, result, row_regions), indent=2))


if __name__ == "__main__":
    main()
