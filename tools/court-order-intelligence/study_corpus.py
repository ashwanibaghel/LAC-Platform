"""Sequential public-PDF corpus inspection; no AI, OCR, database, or retained PDFs."""
import argparse
import hashlib
import json
import re
import tempfile
from pathlib import Path
from urllib.parse import urlsplit

import fitz
import requests

TOPICS = {
    "reference": r"section\s*18|reference.{0,35}(?:court|ADJ|district judge)",
    "apportionment": r"apportion|section\s*30|section\s*31",
    "compensation": r"compensation|disburse|deposit|release",
    "possession": r"possession|section\s*24|acquisition.{0,30}laps",
    "filing": r"affidavit|status report|counter.affidavit",
    "records": r"award|khasra|notification|demarcation",
    "compliance": r"compli|filed|placed on record",
}

def official_url(url):
    if urlsplit(url).hostname == "dhccaseinfo.nic.in":
        url = "https://delhihighcourt.nic.in/app/showFileJudgment/" + urlsplit(url).path.split("/")[-1]
    return url


def inspect(url):
    url = official_url(url)
    parsed = urlsplit(url)
    if parsed.scheme != "https" or parsed.hostname not in {"delhihighcourt.nic.in", "dhccaseinfo.nic.in"} or parsed.query:
        raise ValueError("Only direct official, query-free PDF URLs are allowed")
    with tempfile.TemporaryDirectory(prefix="lac-court-study-") as temporary:
        pdf = Path(temporary) / "official.pdf"
        with requests.get(url, stream=True, timeout=(10, 60), allow_redirects=False) as response:
            response.raise_for_status()
            if response.status_code != 200:
                raise ValueError("Direct PDF response required")
            digest = hashlib.sha256()
            size = 0
            with pdf.open("wb") as output:
                for chunk in response.iter_content(65536):
                    size += len(chunk)
                    if size > 30 * 1024 * 1024:
                        raise ValueError("PDF exceeds study cap")
                    digest.update(chunk)
                    output.write(chunk)
        pages = []
        with fitz.open(pdf) as document:
            for number, page in enumerate(document, 1):
                text = re.sub(r"\s+", " ", page.get_text())
                topics = [key for key, pattern in TOPICS.items() if re.search(pattern, text, re.I)]
                if topics:
                    matches = list(re.finditer(r"Land Acquisition Collector|\bLAC\b|is directed|shall|status report|reference petition", text, re.I))
                    passages = [text[max(0, m.start()-100):m.start()+500] for m in matches[:3]] or [text[-850:]]
                    pages.append({"page": number, "topics": topics, "passages": passages})
            first = re.sub(r"\s+", " ", document[0].get_text())
            count = len(document)
        if not any(re.search(r"Land Acquisition|\bLAC\b", passage, re.I) for page in pages for passage in page["passages"]):
            raise ValueError("Not a LAC-focused order")
        return {"officialUrl": url, "sha256": digest.hexdigest(), "pageCount": count, "firstPage": first[:2400], "relevantPages": pages, "temporaryPdfDeleted": True}
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("candidates")
    parser.add_argument("--output", required=True)
    parser.add_argument("--refresh", action="store_true")
    args = parser.parse_args()
    urls = json.loads(Path(args.candidates).read_text(encoding="utf-8"))
    previous = json.loads(Path(args.output).read_text(encoding="utf-8")) if Path(args.output).exists() else []
    cached = {row["officialUrl"]: row for row in previous if "error" not in row} if not args.refresh else {}
    results = []
    for i, url in enumerate(urls, 1):
        url = official_url(url)
        try:
            entry = cached.get(url) or inspect(url)
            results.append(entry)
            print(f"{i}/{len(urls)} inspected {entry['pageCount']} pages", flush=True)
        except Exception as error:
            results.append({"officialUrl": url, "error": str(error)})
            print(f"{i}/{len(urls)} unavailable: {type(error).__name__}", flush=True)
        Path(args.output).write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

if __name__ == "__main__":
    main()
