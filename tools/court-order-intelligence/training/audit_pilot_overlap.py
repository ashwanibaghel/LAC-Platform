"""Read-only full-native-order overlap audit, excluding court boilerplate.

No source labels or split assignments are changed. Any cross-protected-boundary
near duplicate is a stop/report condition, not permission to repair a blind set.
"""
import json
import re
from foundation import ROOT
from leakage import near_duplicate
from anchors import source_header
from annotations.pilot_review import SOURCES


def body(record):
    _, pages = source_header({int(p): text for p, text in record["pages"].items()})
    if pages is None:
        raise ValueError("Order boundary absent: manual full-order overlap review required")
    selected = []
    for text in pages.values():
        text = re.split(r"This is a digitally signed order|Signature Not Verified", text)[0]
        text = re.sub(r"W\.P\.\(C\)\s+\d+/\d{4}\s+Page\s+\d+\s+of\s+\d+", "", text)
        selected.append(text)
    return " ".join(selected)


if __name__ == "__main__":
    inventory = json.loads((ROOT / "pilot-v1/splits.json").read_text())["matter_inventory"]["matters"]
    entries = [(version, source["matter_id"], body(json.loads((ROOT / "local-private/pilot-source-audit" / (version + ".json")).read_text(encoding="utf-8"))))
               for version, source in SOURCES.items()]
    overlaps = []
    for i, (version, matter, text) in enumerate(entries):
        for other, owner, content in entries[i + 1:]:
            if matter != owner and near_duplicate(text, content):
                overlaps.append({"versions": [version, other], "matters": [matter, owner],
                    "cross_split": inventory[matter]["split"] != inventory[owner]["split"]})
    print(json.dumps({"full_native_order_count": len(entries), "cross_matter_near_duplicates": overlaps}, indent=2))
    if any(r["cross_split"] for r in overlaps):
        raise ValueError("Full-source overlap crosses frozen boundaries; evaluation is not pristine")
