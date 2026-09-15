"""Local-only scored sample for the NM printed-layer feasibility pass."""
from __future__ import annotations

from pathlib import Path
import json
import re


ROOT = Path(r"C:\LAC-Platform\tmp\nm-feasibility")

# Ground truth was transcribed only where the source page is visually readable.
# None deliberately means GroundTruthUnreadable, never a guessed correction.
SAMPLE = [
    (1, "1a", 260, 480, "Iqbal Singh S/o Aman Singh", "1/3", None, None, None),
    (1, "1b", 480, 680, "Satyender Singh S/o Aman Singh", "1/3", None, None, None),
    (1, "1c", 680, 850, None, None, None, None, None),
    (10, "18", 220, 470, "Suraj Bhan S/o Tek Chand", "1/12", None, "19-16", "732966.94"),
    (20, "36", 210, 430, "Hari Om S/o Mehtab Singh", "1/36", "7", "19-16", "732966.94"),
    (20, "37", 430, 730, "Smt Basanti M/o Ram Rattan", "1/6", "1", "0-7", "25912.97"),
    (30, "52", 200, 470, "Ajit Singh S/o Kanwal Singh", "1/4", None, None, None),
    (30, "53", 470, 790, "Prabhu Dayal S/o Kanwal Singh", "1/4", "2", "5-6", "587619.31"),
    (40, "70", 335, 640, "Rajender Singh S/o Khadak Singh", "1/3", "5", "12-0", "1776053"),
    (50, "86", 190, 430, "Suraj Bhan S/o Nafe Singh", "1/15", "8", "11-3", "329796.59"),
    (50, "87", 430, 690, "Vijay Pal S/o Nafe Singh", "1/15", "8", "11-3", "329796.59"),
    (60, "105", 190, 440, "Umrao Singh S/o Jai Narain", "1/72", "6", "12-8", "75014.98"),
    (60, "106", 440, 730, "Bharat Singh S/o Jaldhare", "1/48", "6", "12-8", "112522.48"),
    (70, "123", 185, 430, "Ram Kumar S/o Chottu Ram", "1/24", "14", "41-9", "767209.09"),
    (70, "124", 430, 730, "Kirti Singh S/o Harkesh Singh", "1/41", "14", "41-9", "460325.45"),
    (75, "133", 190, 430, "Kaptan Singh S/o Kudia", "1/16", "14", "41-9", "1150813.63"),
    (75, "134", 430, 710, "Randhir Singh S/o Kudia", "1/16", "14", "41-9", "1150813.63"),
]


def norm(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", value.lower())


def field_result(expected: str | None, text: str, kind: str) -> str:
    if expected is None:
        return "GroundTruthUnreadable"
    wanted = norm(expected)
    actual = norm(text)
    if wanted in actual:
        return "ExactUseful"
    if kind == "person":
        tokens = [norm(token) for token in expected.split() if len(norm(token)) >= 3 and norm(token) not in {"son", "daughter"}]
        if sum(token in actual for token in tokens) >= 2:
            return "Useful"
    return "WrongOrManualRequired"


def strict_khasra(value: str) -> str | None:
    value = re.sub(r"\s+", " ", value.strip().lower())
    match = re.fullmatch(r"([1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s+min)?", value)
    if not match:
        return None
    return match.group(1) + (" min" if value.endswith(" min") else "")


def main() -> None:
    data = json.loads((ROOT / "rapidocr-measurement.json").read_text(encoding="utf-8"))
    master_data = json.loads((ROOT / "pochanpur-khasras.json").read_text(encoding="utf-8"))
    master = {strict_khasra(item["displayNumber"]) for item in master_data["items"]}
    master.discard(None)
    rows = []
    for page, label, start, end, person, share, khasra, area, amount in SAMPLE:
        words = [word for word in data["pages"][str(page)]["words"] if start <= word["box"]["y"] < end]
        ordered = sorted(words, key=lambda x: (round(x["box"]["y"] / 18), x["box"]["x"]))
        text = " ".join(word["text"] for word in ordered)
        person_text = " ".join(word["text"] for word in ordered if 70 <= word["box"]["x"] < 290)
        area_text = " ".join(word["text"] for word in ordered if 285 <= word["box"]["x"] < 435)
        amount_text = " ".join(word["text"] for word in ordered if word["box"]["x"] >= 1060)
        # Raw field scoring happens before any parser gate. The next step alone
        # applies the production strict Khasra grammar.
        khasra_tokens = [word["text"] for word in ordered if 210 <= word["box"]["x"] < 300 and re.search(r"\d", word["text"])]
        person_result = field_result(person, person_text, "person")
        share_result = field_result(share, person_text, "share")
        area_result = field_result(area, area_text, "area")
        amount_result = field_result(amount, amount_text, "amount")
        raw_khasra = "GroundTruthUnreadable" if khasra is None else ("ExactUseful" if any(norm(value) == norm(khasra) for value in khasra_tokens) else "WrongOrManualRequired")
        normalized = next((strict_khasra(value) for value in khasra_tokens if khasra is not None and norm(value) == norm(khasra) and strict_khasra(value) is not None), None)
        canonical = normalized in master if normalized else False
        resolved = [value for value in (person_result, share_result, raw_khasra, area_result, amount_result) if value != "GroundTruthUnreadable"]
        grouping = "Whole" if resolved and all(value == "ExactUseful" for value in resolved) else ("Partial" if any(value in {"ExactUseful", "Useful"} for value in resolved) else "Wrong")
        rows.append({"page": page, "record": label, "person": person_result, "khasraRaw": raw_khasra,
                     "khasraNormalized": normalized is not None, "khasraMasterExact": canonical,
                     "share": share_result, "area": area_result, "amount": amount_result, "grouping": grouping})
    def counts(key: str) -> dict[str, int]:
        output: dict[str, int] = {}
        for row in rows:
            output[row[key]] = output.get(row[key], 0) + 1
        return output
    output = {"records": len(rows), "pages": sorted({row["page"] for row in rows}), "person": counts("person"),
              "khasraRaw": counts("khasraRaw"), "khasraNormalizedValid": sum(row["khasraNormalized"] for row in rows),
              "khasraMasterExact": sum(row["khasraMasterExact"] for row in rows), "area": counts("area"),
              "share": counts("share"), "amount": counts("amount"), "grouping": counts("grouping"), "rows": rows}
    (ROOT / "ground-truth-score.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(json.dumps({key: output[key] for key in output if key not in {"rows"}}, indent=2))


if __name__ == "__main__":
    main()
