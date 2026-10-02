"""Additional frozen qualitative officer questions; never used for training.

Authored from already-read sources before inspecting any Pilot model output.
These are extractive ID contracts: officer prose is exact attributed source text,
not a new generated legal narrative. Available chains are explicitly incomplete.
"""
import sys
from pathlib import Path
sys.path[:0] = [str(Path(__file__).resolve().parents[1]), str(Path(__file__).resolve().parents[2]), str(Path(__file__).resolve().parents[1] / "kaggle")]
from annotations.pilot_review import PASSAGES, SOURCES, REVIEW

CASES = {
 "6384": (["wpc6384-2024-march2025-p0", "wpc6384-2024-march2025-p1", "wpc6384-2024-july2025-p2"] + [f"wpc6384-2024-p{i}" for i in range(5)], [
  ("scene", "In teen supplied aadeshon ka poora scene aur latest position batao; adhura source history complete mat batao.", [3, 4, 5, 7]),
  ("last-three", "In teen available hearings mein filing aur costs ke bare mein kya badla? Chronological sequence batao.", [0, 1, 2, 3, 4, 5, 7]),
  ("action", "Agli recorded hearing se pehle LAC office ko kya karna hai? Conditional attendance ko unconditional mat banao.", [4, 5]),
  ("compliance", "Kya latest order mein LAC ke costs deposit karne ki completion confirm hui?", []),
  ("next", "Latest supplied order mein agli Court listing kab record hai?", [7]),
  ("demand", "Petitioner ki actual original relief kya thi? Sirf supplied passages use karo.", [])]),
 "6203": (["wpc6203-2026-may6-p0", "wpc6203-2026-may29-p1"] + [f"wpc6203-2026-p{i}" for i in range(6)], [
  ("scene", "Is case ki latest supplied position batao: party stand, compensation finding, stay aur current affidavit direction alag rakho.", [3, 4, 5, 6]),
  ("demand", "Petitioners ki grievance kya thi? Usko Court finding mat kehna.", [0]),
  ("lac-stand", "Latest order mein LAC ke counsel ne affidavit ke bare mein kya kaha?", [3]),
  ("compensation", "Muawza ka basis Court ne kya kaha?", [4]),
  ("action", "LAC ke liye latest current affidavit direction kya hai, aur kya usme condition hai? Purane quoted order ko naya action mat banao.", [6]),
  ("compliance", "Kya in supplied sources mein LAC affidavit filed hone ki confirmed completion hai?", []),
  ("next", "Latest supplied order ki Court listing date kya hai?", [7])]),
 "11284": (["wpc11284-2026-aug7-p0", "wpc11284-2026-aug7-p1"] + [f"wpc11284-2026-p{i}" for i in range(6)], [
  ("scene", "Latest supplied case position mein undertaking, demarcation aur interim protection ka scene batao.", [2, 3, 4, 5]),
  ("demand", "Petitioner ne kya conditional undertaking offer ki thi?", [0]),
  ("compliance", "Affidavit file hua tha ya sirf time mila tha? Court ne kya completion record ki?", [2]),
  ("possession", "Kya precise location conclusively established hai ya demarcation pending hai?", [3]),
  ("action", "Respondents ko abhi demarcation ke liye kya karna hai?", [4]),
  ("vacation", "Kya six months ko Court ne final unconditional vacation deadline banaya? Latest position kya hai?", [6]),
  ("next", "Latest available order mein next Court listing kya hai?", [7])]),
 "8404": ([f"wpc8404-2024-p{i}" for i in range(5)], [
  ("scene", "Is supplied case ka scene batao: challenge, Court finding, current reference direction aur disposal.", [0, 2, 3, 4]),
  ("finding", "Court ne LAC ke delay par application dismiss karne ke bare mein kya kaha?", [2]),
  ("action", "LAC ko current order ke hisaab se reference kitne samay mein aur kis note ke saath bhejna hai? Quoted six weeks current deadline nahi hai.", [3]),
  ("compliance", "Kya LAC ne reference actually forward kar diya, ye confirm hai?", []),
  ("last-three", "Pichhli teen actual hearings mein kya hua tha? Quoted other-case precedent ko is case ki hearing mat banao.", []),
  ("next", "Is disposed matter ki agli hearing date source se establish hai?", [])]),
}


def records():
    from foundation import evidence, canonical
    from types import SimpleNamespace
    from prepare_smoke_dataset import QA_INSTRUCTIONS
    context = SimpleNamespace(PASSAGES=PASSAGES, SOURCES=SOURCES)
    rows = []
    for key, (ids, queries) in CASES.items():
        bound = [evidence(pid, context) for pid in ids]
        if len({p["matter_id"] for p in bound}) != 1 or len(bound) > 8:
            raise ValueError("Invalid officer evaluation evidence scope")
        for suffix, query, selected in queries:
            value = {"currentCase": bound[0]["matter_id"], "question": query,
                "availableEvidence": [{"factId": i, "text": p["text"], "category": PASSAGES[ids[i]][3],
                    "scope": PASSAGES[ids[i]][5], "source": {"orderDate": p["order_date"], "page": p["page"],
                    "evidence": p["text"], "officialUrl": p["url"]}} for i, p in enumerate(bound)]}
            expected = {"claims": [{"factId": i} for i in selected]}
            rows.append({"id": f"officer-{key}-{suffix}", "task": "officer_qualitative_qa", "matter_id": bound[0]["matter_id"],
                "language": "Hinglish", "contract": "claims", "input": value, "expected": expected,
                "messages": [{"role": "system", "content": QA_INSTRUCTIONS}, {"role": "user", "content": canonical(value)},
                             {"role": "assistant", "content": canonical(expected)}], "review": REVIEW,
                "frozen_before_model_output_inspection": True, "training_eligible": False})
    return rows


if __name__ == "__main__":
    from foundation import ROOT, write_jsonl
    write_jsonl(ROOT / "pilot-v1/officer-evaluation.jsonl", records())
    print(f"Frozen {len(records())} officer questions; not training data")
