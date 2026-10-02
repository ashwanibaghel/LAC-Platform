"""Bounded public-PDF acquisition for local source audit; never assigns gold.

Private workbook inventory is used only locally. Only native source text and
public provenance are retained in ignored local-private; temporary PDFs are
removed even on read failure. No search form, CAPTCHA or model is invoked.
"""
import argparse
import json
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent))
from worker import download, native_pages
from foundation import write_json


def acquire(case, url):
    with tempfile.TemporaryDirectory(prefix="lac-pilot-source-") as directory:
        path, sha = download(url, directory)
        pages = native_pages(path)
        return {"case": case, "url": url, "sha256": sha,
                "pages": pages, "audit_state": "UNREVIEWED",
                "coverage": "Supplied public source only; not complete order history"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("case")
    parser.add_argument("url")
    parser.add_argument("--id", required=True)
    args = parser.parse_args()
    if not args.id.replace("-", "").isalnum():
        raise ValueError("Simple audit identifier required")
    record = acquire(args.case, args.url)
    destination = ROOT / "local-private/pilot-source-audit" / (args.id + ".json")
    write_json(destination, record)
    print(json.dumps({"case": args.case, "sha256": record["sha256"],
                      "page_count": len(record["pages"]), "audit_path": str(destination)}))


if __name__ == "__main__":
    main()
