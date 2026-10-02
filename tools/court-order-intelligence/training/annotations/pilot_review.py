"""Pilot V1 independently audited native passages; no inference output imported.

All pages of the 20 acquired versions were read by Codex before split freeze.
Register status is candidate provenance, NOT a verified current court status.
Coverage is bounded to supplied PDFs and explicitly cited prior actual orders;
no complete-history claim or invented order on a prospective listing date.
"""
import json
from pathlib import Path

REVIEW = {"reviewer": "Codex independent full-native-source audit",
          "method": "Temporary official PDF SHA + all native pages read; explicit source-bound decisions",
          "reviewed_on": "2026-10-02", "annotation_version": "pilot-v1-native-audit-1"}
LEDGER = Path(__file__).with_name("pilot_sources.json")
SOURCES = json.loads(LEDGER.read_text(encoding="utf-8")) if LEDGER.exists() else {}

# page, verbatim normalized passage, semantic role, runtime field, scope, actor.
REVIEWED = {
 "wpc7003-2026": ("2026-05-20", [
  (3, "11. The present petition has been filed at this stage on the ground that the boundary wall situated on the Petitioners’ land is being demolished and, therefore, an interim order is sought.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (4, "(iii) The compensation amount of Rs.4,42,41,462/- has already been deposited in terms of the Section 3H(4) of the NHAI Act, before the reference court, ld. ADJ, Patiala House Courts, New Delhi.", "OTHER_PARTY_SUBMISSION", "context", "Current", "NHAI"),
  (6, "20. The notification under Section 3D of the NHAI Act has been issued and the Award has also been passed. The Petitioners are also aware that the compensation stands deposited. The compensation cannot be released to the Petitioners till the civil disputes between them and the recorded owners of the subject land are resolved.", "COURT_FINDING", "finding", "Current", "Court"),
  (6, "21. Under these circumstances, the prayer for interim relief is not made out at this stage.", "COURT_FINDING", "finding", "Current", "Court"),
  (7, "25. Counter affidavits, if any, be filed by all the Respondents within a period of six weeks after completion of service. Rejoinder, thereto, be filed within six weeks, thereafter.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (7, "27. List before Court on 28th September, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6384-2024": ("2026-07-16", [
  (1, "4. Costs have not yet been deposited by the LAC in terms of order dated 28th July, 2025.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "LAC"),
  (1, "5. As a last opportunity, let costs be deposited in terms of the previous order dated 28th July, 2025.", "COURT_DIRECTION", "direction", "Current", "LAC"),
  (1, "6. If costs are not paid by the next date, the concerned officer from the LAC office shall remain present in the Court on the next date.", "COURT_DIRECTION", "direction", "Current", "LAC"),
  (1, "7. Rejoinder be filed by the petitioners within four weeks.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
  (1, "8. List on 1st October, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6384-2024-july2025": ("2025-07-28", [
  (1, "1. This Court vide order dated 28th March 2025 ordered closure of respondent’s right to file counter affidavit, despite being granted last opportunity. Counsel for respondent has sought re-opening of this right.", "CASE_CONTEXT", "context", "Historical", "Respondent"),
  (1, "2. Neither the reply nor written application to that effect has been placed on record.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "Respondent"),
  (1, "3. That being said, the same be placed on record subject to costs of Rs.25,000/- to be paid by each of the respondents i.e. respondent nos.1 & 2 and deposited before the Delhi High Court Legal Services Committee within a period of 2 weeks from today.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (2, "5. List on 11th September 2025.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6384-2024-march2025": ("2025-03-28", [
  (1, "Counter affidavit has not been filed by the respondents despite last opportunity having been granted.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "Respondent"),
  (1, "Accordingly, right of respondents to file the counter affidavit is hereby closed.", "PROCEDURAL_EVENT", "filing", "Current", "Court"),
  (1, "List the matter before the Hon’ble Court on 02.04.2025, the date already fixed, for further directions.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "cont659-2018": ("2025-10-28", [
  (1, "1. Learned counsel for the respondents submit that SLP (Civil) field vide [Diary No(s) 25323/2025] is listed before the Hon’ble Supreme Court on 29.10.2025.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "Respondent"),
  (1, "2. As such, re-notify on 03.02.2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "lpa543-2026": ("2026-08-21", [
  (1, "1. The Hon’ble Division Bench has not assembled today, as Hon’ble the Chief Justice is on leave today.", "PROCEDURAL_EVENT", "context", "Current", "Court"),
  (1, "2. List on 29.09.2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6328-2026": ("2026-05-08", [
  (2, "4. The Petitioner asserts ownership and possession over 9 Bighas 12 Biswas of land comprised in Khasra Nos. 53/2/1 (1-07), 9 (4-1) and 12 (4- 4), situated in the revenue estate of Village Bijwasan, NCT of Delhi.1", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (3, "11. Mr. N.S. Vashisht, counsel for the Petitioner, submits that no notification initiating acquisition proceedings under the 2013 Act has been issued till date qua the subject land.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (4, "13. In the aforesaid circumstances, it is directed that status quo with respect to possession of the subject land shall be maintained till the next date of hearing.", "COURT_DIRECTION", "direction", "Current", "Respondent"),
  (4, "In case DDA were to initiate acquisition proceedings in accordance with law and within the framework of the decision passed by the Supreme Court, the present order shall not preclude the Respondents from taking further steps in accordance with law.", "COURT_FINDING", "finding", "Current", "Court"),
  (4, "15. Re-notify on 07th October, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6203-2026": ("2026-08-21", [
  (2, "14. Accordingly, the LAC and Delhi Jal Board, which is the agency for whom the land has been acquired, shall obtain instructions in this matter on these two aspects and file short affidavits by the next date of hearing.", "COURT_DIRECTION", "filing", "Quoted", "LAC"),
  (3, "7. Today, the stand of Mr. Pathak, ld. Counsel appearing for the LAC is that he has received certain instructions, however, the affidavit could not be filed.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "LAC"),
  (5, "11. A perusal of the above would show that the compensation amount had to be based on the fair market value of the land, and the same is to be treated as urban area land, though it is presently being used for agriculture.", "COURT_FINDING", "finding", "Current", "Court"),
  (6, "18. Under these circumstances, any further proceedings pursuant to the impugned Notification under Section 11 of the Act, 2013 shall remain stayed.", "COURT_DIRECTION", "direction", "Current", "Respondent"),
  (6, "19. The LAC, DJB as also the DDA shall file their affidavits by 10th September, 2026, if they wish to do so, along with the final SIA report.", "COURT_DIRECTION", "filing", "Current", "LAC"),
  (6, "21. List on 14th September, 2026 as Item No.1.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6203-2026-may6": ("2026-05-06", [
  (3, "7. The grievance of the Petitioners is that thereafter, no final SIA study report was published.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (4, "13. The Court has considered the matter. The first and the foremost question would be whether the final SIA report was, in fact, prepared and whether the same was published and the copy was given to the Petitioners or not.", "ISSUE_BEFORE_COURT", "issue", "Current", "Court"),
  (5, "14. Accordingly, the LAC and Delhi Jal Board, which is the agency for whom the land has been acquired, shall obtain instructions in this matter on these two aspects and file short affidavits by the next date of hearing.", "COURT_DIRECTION", "filing", "Current", "LAC"),
  (5, "16. List on 28th May, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc6203-2026-may29": ("2026-05-29", [
  (1, "2. This matter has been taken up today as 28th May, 2026 was declared a holiday on account of Id-ul-Zuha (Bakrid) vide Notification No. 77/I-G- 4/Genl.-I/DHC dated 26th May, 2026.", "PROCEDURAL_EVENT", "context", "Current", "Court"),
  (1, "3. The LAC and Delhi Jal Board have not obtained instructions in this matter and have also not filed any affidavit.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "LAC"),
  (1, "4. Last opportunity is granted to them for filing the same by 10th July, 2026.", "COURT_DIRECTION", "filing", "Current", "LAC"),
  (2, "6. List on 21st August, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc8971-2025": ("2026-04-22", [
  (1, "1. Pleadings are not complete.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "Respondent"),
  (1, "Counter affidavit be filed within a period of six weeks.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (1, "Rejoinder, thereto, if any, be filed within four weeks thereafter.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
  (1, "2. Re-notify on 15th October, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "cont527-2018": ("2025-12-24", [
  (1, "1. Learned counsel for the petitioners seeks a passover.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (1, "2. Considering that this Bench has to rise early, passover is not possible today.", "PROCEDURAL_EVENT", "context", "Current", "Court"),
  (1, "3. In the interest of justice, renotify on 17.03.2026", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc11284-2026": ("2026-08-11", [
  (1, "1. In compliance with the order dated 7th August, 2026, the Petitioner has filed an affidavit along with an undertaking.", "RECORDED_COMPLIANCE", "compliance", "Current", "Petitioner"),
  (2, "The material presently available, therefore, indicates the general location of the land and the Khasra numbers involved, but does not conclusively establish the precise location of Jhuggi No. 42.", "COURT_FINDING", "finding", "Current", "Court"),
  (2, "4. The Respondents are accordingly directed to complete the demarcation through the TSM/DGPS method within three weeks from today. The exercise shall identify, with reference to the revenue record and the relevant Khasra boundaries, the precise location of Jhuggi No. 42. A report, accompanied by the relevant plan/map, shall be placed before the Court and an advance copy furnished to counsel for the Petitioner.", "COURT_DIRECTION", "direction", "Current", "Respondent"),
  (3, "6. Until the demarcation exercise is completed and considered by this Court, the operation of the impugned notice dated 29th July, 2026 shall remain in abeyance qua the Petitioner.", "COURT_DIRECTION", "direction", "Current", "Respondent"),
  (3, "The period within which such vacation is to take place shall, however, be determined by this Court.", "COURT_FINDING", "finding", "Current", "Court"),
  (3, "9. Re-notify on 9th September, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc11284-2026-aug7": ("2026-08-07", [
  (2, "3. During the hearing, Mr. Tishampati Sen, counsel appearing for the Petitioner, states that the Petitioner is willing to place an undertaking on affidavit that, if the official demarcation and the relevant revenue record establish that Jhuggi No. 42 falls within the land covered by the impugned notice, she would vacate the premises peacefully.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (3, "6. Accordingly, the Petitioner shall place the proposed undertaking on affidavit before the next date of hearing.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
  (3, "The Respondents shall, in the meanwhile, place on record the joint inspection report dated 10th July, 2026, along with the relevant demarcation report, shajra/site plan and other revenue records, if available, indicating the location of Jhuggi No. 42 vis-à- vis the Khasra numbers mentioned in the impugned notice.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (3, "7. List on 11th August, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "cs147-2022": ("2026-03-19", [
  (1, "1. A request for passover is made on behalf of the Plaintiff.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (1, "2. In view of the fact that there are already about 15 passovers, this Court is not inclined to grant further Passovers today.", "PROCEDURAL_EVENT", "context", "Current", "Court"),
  (1, "3. List on 18.08.2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "cs23-2025": ("2026-07-13", [
  (1, "It is submitted by contemptnor no.5 that she has filed Reply to the present contempt petition and advance copy has been supplied to Ld. counsel for petitioner.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "Respondent"),
  (1, "However, Ld. counsel for petitioner states that she has not received copy of Reply.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (1, "Ld. counsel for Contemptnor no.5 is directed to supply the same again to Ld. counsel for petitioner.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (2, "It is submitted by Ld. Counsel for plaintiff that on the next date of hearing, they will be arguing on the present applications and will be filing reply thereto.", "PETITIONER_SUBMISSION", "context", "Current", "Petitioner"),
  (2, "Be fixed for arguments on the aforesaid IAs on 04.11.2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc16669-2024": ("2025-12-17", [
  (1, "List on 15.04.2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc2466-2026": ("2026-02-20", [
  (2, "4. Counsel for the Respondents submits that since the land is under consolidation proceedings, the Sub-Registrar could not proceed with registration without verification and permission from the competent authority.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "Respondent"),
  (3, "(i) The Petitioners shall file an undertaking by way of an affidavit stating that the factum of this order shall be disclosed in the event of any further transaction relating to the subject land, prior to the conclusion of the consolidation proceedings. Let the said undertaking be filed within a period of two weeks from today. Upon filing of such an undertaking, the Petitioners shall remain bound thereby.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
  (3, "(iii) Subject to verification of the land acquisition status by the competent authority, and in line with the decisions in Okaya Infocom Pvt. Ltd. and Jeevantika Organic Farming LLP, it is directed that the registration of the Sale Deed, pending before Respondent No. 3, shall not be refused solely on the ground of pendency of consolidation proceedings or non-availability of any sanction in that regard, and shall be processed further in accordance with law.", "COURT_DIRECTION", "direction", "Current", "Sub-Registrar"),
  (4, "9. With the following directions, the petition is disposed of along with pending application(s), if any.", "DISPOSITION", "disposition", "Current", "Court")]),
 "wpc10308-2024": ("2026-08-13", [
  (3, "6. Pursuant to the latest directions, GNCTD has now filed an affidavit based upon the status report dated 21st July, 2026.", "RECORDED_COMPLIANCE", "compliance", "Current", "GNCTD"),
  (3, "Instead of acknowledging a deficiency of 7 bighas 15 biswas, GNCTD now asserts that, upon reconciliation of the record, excess land measuring approximately 28 bighas has been allotted to the Petitioner or his predecessor-in-interest and the same is liable to be recovered.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "GNCTD"),
  (4, "The latter position may ultimately be borne out by the original record, but it cannot simply displace the earlier admission without explaining, parcel by parcel, where the earlier computation went wrong.", "COURT_FINDING", "finding", "Current", "Court"),
  (5, "v. In view of the materially different positions taken by GNCTD at different stages of these proceedings, the final reconciliation shall also explain the basis for the departure, if any, from the computation contained in the earlier counter affidavit.", "COURT_DIRECTION", "direction", "Current", "GNCTD"),
  (6, "14. The entire exercise shall be completed within a period of ten weeks from today.", "COURT_DIRECTION", "direction", "Current", "GNCTD"),
  (7, "17. The petition, along with the pending application(s), is disposed of in the above terms.", "DISPOSITION", "disposition", "Current", "Court")]),
 "wpc10308-2024-sep2025": ("2025-09-23", [
  (2, "3. A joint status report, as directed above, has not been filed.", "DOCUMENT_OR_FILING_FACT", "filing", "Current", "GNCTD"),
  (3, "5. The result is that six weeks later, we are still at the stage of looking for the record. I am not satisfied with this position.", "COURT_OBSERVATION", "observation", "Current", "Court"),
  (4, "8. In the event the concerned District Magistrate and the Commissioner (Land Management) DDA, are unable to resolve the issue within a period of two weeks from today, the issue be escalated to the Vice Chairman, DDA, and the Divisional Commissioner, GNCTD.", "COURT_DIRECTION", "direction", "Current", "GNCTD"),
  (4, "9. A joint status report be filed within six weeks from today.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
  (4, "10. List on 19.11.2025.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc10308-2024-feb2026": ("2026-02-06", [
  (1, "2. The record has been perused. On the basis of the pleadings and the counter affidavits placed on record, there appears to be no substantial dispute as to the Petitioner’s entitlement to restoration/allotment corresponding to the deficit of land.", "COURT_OBSERVATION", "observation", "Current", "Court"),
  (2, "5. Ms. Avni Singh, panel counsel appearing for the GNCTD, submits that the matter is under consideration at the departmental level.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "GNCTD"),
  (3, "7. The competent officer of GNCTD, duly authorised and conversant with the record, shall file a short affidavit answering the above queries, along with relevant extracts of the record.", "COURT_DIRECTION", "filing", "Current", "GNCTD"),
  (3, "8. List on the date already fixed i.e., 14th April, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")]),
 "wpc8404-2024": ("2026-05-29", [
  (1, "2. The present petition challenges the impugned order dated 22nd February, 2024 passed by the LAC, District South West, Delhi, by which the application filed by the Petitioner dated 30th March 2012 under Section 18 of the Land Acquisition Act has been rejected on the ground of delay.", "CASE_CONTEXT", "context", "Current", "Petitioner"),
  (4, "The LAC shall also forward the reference to the competent court within six weeks.", "COURT_DIRECTION", "direction", "Quoted", "LAC"),
  (5, "9. As held in the above orders, the LAC cannot itself dismiss the application on the ground of delay. At best the LAC can send the matter to the Reference Court, with its opinion that the application is barred.", "COURT_FINDING", "finding", "Current", "Court"),
  (5, "10. Accordingly, the impugned order is set aside. Let the application filed by the Petitioner under Section 18 of the Land Acquisition be sent by the LAC to the appropriate reference Court within a period of one month with a note that according to the LAC, the application is barred by limitation.", "COURT_DIRECTION", "direction", "Current", "LAC"),
  (5, "11. The petition along with pending application(s), if any, is disposed of in these terms.", "DISPOSITION", "disposition", "Current", "Court")]),
}

PASSAGES = {f"{version}-p{i}": (version, *passage)
            for version, (_, passages) in REVIEWED.items()
            for i, passage in enumerate(passages)}
MATTERS = {s["matter_id"]: {"classification": "PILOT", "source_status": s["register_status"],
                          "connections": [], "common_judgments": []}
           for s in SOURCES.values()}
# Related precedent source sets are recorded even when the cited case is absent
# from this pilot. It cannot later be introduced across a protected boundary.
if "W.P.(C) 8404/2024" in MATTERS:
    MATTERS["W.P.(C) 8404/2024"]["connections"] = ["W.P.(C) 2686/2018", "W.P.(C) 7817/2017", "W.P.(C) 5796/2026"]
if "W.P.(C) 10308/2024" in MATTERS:
    MATTERS["W.P.(C) 10308/2024"]["connections"] = ["W.P.(C) 7687/2004"]

EXAMPLES = []
# Explicitly reviewed template decisions. Negative completion means NO claim of
# completed LAC action; filing directions and party assertions cannot prove it.
for version, (date, raw) in REVIEWED.items():
    supplied = [f"{version}-p{i}" for i in range(len(raw))]
    action = [pid for pid in supplied if PASSAGES[pid][3] == "COURT_DIRECTION"
              and PASSAGES[pid][5:] == ("Current", "LAC")]
    completion = [pid for pid in supplied if PASSAGES[pid][3] == "RECORDED_COMPLIANCE"]
    definitions = [
      ("extract", "semantic_proposition_extraction", supplied, "", "English", "SUPPORTED"),
      ("attribute", "attribution_classification", supplied, "", "English", "SUPPORTED"),
      ("important", "important_fact_selection", supplied, "Select the important source-backed facts, preserving roles and conditions.", "English", "SUPPORTED"),
      ("action", "office_action_detection", action, "Which current direction is explicitly assigned to LAC? Exclude generic respondents and quoted prior orders.", "English", "SUPPORTED" if action else "NONE"),
      ("compliance", "compliance_state", completion, "What completion does the Court actually record, and for which actor? A filing direction or party assertion is not completed compliance.", "English", "SUPPORTED" if completion else "NOT_CONFIRMED_COMPLETE"),
      ("digest", "order_digest", supplied, "Is supplied order ka source-bound digest do; party stand aur Court finding alag rakho.", "Hinglish", "SUPPORTED"),
      ("date-en", "date_specific_retrieval_or_QA", supplied, f"What happened in the supplied order dated {date}?", "English", "SUPPORTED"),
      ("date-hi", "date_specific_retrieval_or_QA", supplied, f"{date} के उपलब्ध आदेश में क्या हुआ? पक्ष का कथन और न्यायालय का निष्कर्ष अलग रखें।", "Hindi", "SUPPORTED"),
      ("date-roman", "date_specific_retrieval_or_QA", supplied, f"{date} ke uplabdh aadesh mein kya hua? Paksh ka kathan aur nyayalaya ka nishkarsh alag rakho.", "RomanHindi", "SUPPORTED"),
      ("unsupported", "date_specific_retrieval_or_QA", [], "Do these passages establish that all compensation has been paid to every landowner and all office obligations are complete?", "English", "INSUFFICIENT_EVIDENCE"),
    ]
    for suffix, task, selected, query, language, outcome in definitions:
        EXAMPLES.append((version + "-" + suffix, task, supplied, selected, query, language, outcome))

# Bounded complete supplied chains: explicitly enumerate, never merge cases.
CHAINS = {
 "10308": (["wpc10308-2024-sep2025", "wpc10308-2024-feb2026", "wpc10308-2024"], ["wpc10308-2024-p0", "wpc10308-2024-p1", "wpc10308-2024-p2", "wpc10308-2024-p3", "wpc10308-2024-p4", "wpc10308-2024-p5"]),
 "6384": (["wpc6384-2024-march2025", "wpc6384-2024-july2025", "wpc6384-2024"], ["wpc6384-2024-p0", "wpc6384-2024-p1", "wpc6384-2024-p2", "wpc6384-2024-p4"]),
 "6203": (["wpc6203-2026-may6", "wpc6203-2026-may29", "wpc6203-2026"], ["wpc6203-2026-p1", "wpc6203-2026-p2", "wpc6203-2026-p3", "wpc6203-2026-p4", "wpc6203-2026-p5"]),
 "11284": (["wpc11284-2026-aug7", "wpc11284-2026"], ["wpc11284-2026-p0", "wpc11284-2026-p1", "wpc11284-2026-p2", "wpc11284-2026-p3", "wpc11284-2026-p4", "wpc11284-2026-p5"]),
}
# Chains bounded to <=8 evidence entries; latest + pertinent earlier context.
for key, (versions, selected) in CHAINS.items():
    prior = [f"{versions[0]}-p0", f"{versions[-2]}-p1"] if len(versions) == 3 else [f"{versions[0]}-p0", f"{versions[0]}-p1"]
    supplied = list(dict.fromkeys(prior + selected))
    EXAMPLES.append((key + "-position", "multi_order_current_position", supplied, selected,
                     "What is the latest recorded position across ONLY these supplied orders? Keep quoted history and party assertions distinct; do not claim complete court coverage.", "English", "SUPPORTED"))

# Explicit review approval is separate from serialization. Adding a new source
# or a new template requires a new reviewed version, not inference promotion.
VERIFIED_VERSIONS = frozenset(REVIEWED)
VERIFIED_SUFFIXES = frozenset({"extract", "attribute", "important", "action", "compliance", "digest", "date-en", "date-hi", "date-roman", "unsupported"})
VERIFIED_IDS = frozenset(f"{v}-{s}" for v in VERIFIED_VERSIONS for s in VERIFIED_SUFFIXES) | {"6384-position", "6203-position", "11284-position", "10308-position"}
QUARANTINED = [
 {"id": "7003-cross-page-panchnama", "state": "QUARANTINED", "reason": "Direction spans PDF pages; single-page evidence contract cannot represent complete sentence safely."},
 {"id": "11284-address-background", "state": "QUARANTINED", "reason": "Private residential-location narrative excluded from public minimized dataset."},
 {"id": "10308-excess-as-established", "state": "REJECTED", "reason": "GNCTD assertion of excess allotment is not a Court-established quantity."},
]

# Full-gold expansion against the frozen runtime failed on these source bundles.
# Preserve the truthful audited labels; do NOT relabel them or relax runtime.
# Claims tasks retain independent source review; these extraction targets alone
# are excluded. Split inventory retains their IDs so protected groups cannot drift.
RUNTIME_INCOMPATIBLE = frozenset({
 "wpc7003-2026", "wpc6384-2024", "wpc6384-2024-july2025", "wpc6384-2024-march2025",
 "cont659-2018", "wpc6328-2026", "wpc6203-2026-may29", "wpc8971-2025", "cont527-2018",
 "wpc11284-2026", "cs23-2025", "wpc10308-2024", "wpc10308-2024-sep2025",
 "wpc10308-2024-feb2026", "wpc8404-2024"})
RUNTIME_EXCLUDED_IDS = frozenset(f"{v}-{suffix}" for v in RUNTIME_INCOMPATIBLE for suffix in ("extract", "attribute"))
VERIFIED_IDS = VERIFIED_IDS - RUNTIME_EXCLUDED_IDS
QUARANTINED += [{"id": key, "state": "QUARANTINED", "reason": "Audited extraction target rejected by unchanged runtime expansion; labels not changed to bypass safety."}
                for key in sorted(RUNTIME_EXCLUDED_IDS)]
