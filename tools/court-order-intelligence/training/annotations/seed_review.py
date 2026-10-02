"""Manually source-reviewed seed, not generated from inference artifacts.

Review: Codex read every native-text page of these exact PDF downloads on
2026-10-02. Text below is verbatim after whitespace normalization only.
This is an independently source-audited development seed, NOT an officer/legal
certification or unseen evaluation set. Future annotations require new review.
"""
REVIEW = {"reviewer": "Codex independent source-passage audit",
          "method": "Exact downloaded PDF SHA + all native pages read; no model-output import",
          "reviewed_on": "2026-10-02", "annotation_version": "t1-source-review-1"}

SOURCES = {
    "14604-july": {
        "matter_id": "W.P.(C) 14604/2025", "order_date": "2026-07-29", "page_count": 1,
        "sha256": "d14f6ae26852bb7d1e2aba8270d9202c2876f8b71433ba15f780396297644c34",
        "url": "https://delhihighcourt.nic.in/app/showlogo/1785910551_2e64789e2b35c836_670_146042025.pdf/2026"},
    "14604-may": {
        "matter_id": "W.P.(C) 14604/2025", "order_date": "2026-05-07", "page_count": 2,
        "sha256": "79dd6f4e23eef6cdc37741f9a601e206fdabf6e5ea6456e265534189c592c5aa",
        "url": "https://delhihighcourt.nic.in/app/downloadOrderbByDate/W.P.%28C%29/14604/2025/07-05-2026"},
    "8664-january": {
        "matter_id": "W.P.(C) 8664/2021", "order_date": "2025-01-30", "page_count": 6,
        "sha256": "a0ddb33e46ce9d1d6d017dd715da57cf57afb0a6e677fa74c91318de721f84a5",
        "url": "https://delhihighcourt.nic.in/app/showFileJudgment/VIB30012025CW86642021_112417.pdf"},
    "940-may": {
        "matter_id": "W.P.(C) 940/2015", "order_date": "2026-05-21", "page_count": 1,
        "sha256": "baa6cd8ba621ddb28776a45546aaa5071b9c02cf55ab835302516bcb75292be1",
        "url": "https://delhihighcourt.nic.in/app/showlogo/1779521222_a5ac29297695be31_pms_9402015.pdf/2026"}}

MATTERS = {
    "W.P.(C) 14604/2025": {"classification": "DEVELOPMENT", "source_status": "Pending", "connections": []},
    "W.P.(C) 8664/2021": {"classification": "DEVELOPMENT", "source_status": "Disposed", "connections": []},
    "W.P.(C) 940/2015": {"classification": "DEVELOPMENT", "source_status": "Pending", "connections": []}}

# source version, page, exact text, role, field, scope, actor. These are the
# reviewed decisions; exporters may serialize but must not infer new labels.
PASSAGES = {
    "jul-counter": ("14604-july", 1, "By way of last and final opportunity, four weeks time is granted to the respondents to file counter affidavit with advance copy to counsel for the petitioner.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
    "jul-rejoinder": ("14604-july", 1, "Rejoinder thereto, if any, be filed within two weeks thereafter.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
    "jul-list": ("14604-july", 1, "List the matter before the Hon’ble Court on 01.10.2026, the date already fixed, for further directions.", "COURT_DIRECTION", "nextHearing", "Current", "Court"),
    "may-request": ("14604-may", 1, "2. Adjournment is sought by Mr. Sunil Kumar Jha, ld. Counsel for the Respondents to file the counter affidavit.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "Respondent"),
    "may-counter": ("14604-may", 1, "3. Let the same be filed within a period of six weeks, in terms of the previous order dated 16th April, 2026.", "COURT_DIRECTION", "filing", "Current", "Respondent"),
    "may-rejoinder": ("14604-may", 1, "Rejoinder be filed within six weeks, thereafter.", "COURT_DIRECTION", "filing", "Current", "Petitioner"),
    "may-registrar": ("14604-may", 1, "4. List before the Joint Registrar on 29th July, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Joint Registrar"),
    "may-list": ("14604-may", 2, "5. List before Court on 1st October, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court"),
    "jan-lac-claim": ("8664-january", 2, "5. The LAC has filed a counter affidavit affirming that on 10.02.2012 a notice under Section 12(2) of the LA Act was issued and the same was ‘duly served upon the petitioner’.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "LAC"),
    "jan-lac-correction": ("8664-january", 3, "6. The learned counsel appearing for the LAC fairly states that the averments made in the counter affidavit may not be entirely correct as the record does not reflect any service of the notice to the petitioner.", "LAC_OR_RESPONDENT_SUBMISSION", "context", "Current", "LAC"),
    "jan-finding": ("8664-january", 3, "7. In view of the above, the respondent’s contention that a notice under Section 12(2) of the LA Act was served on the petitioner must be rejected and the petitioner’s contention that he has not received any notice must be accepted.", "COURT_FINDING", "finding", "Current", "Court"),
    "jan-limitation": ("8664-january", 6, "10. In view of the above, the reference under Section 18 of the LA Act could not be rejected on the ground that it was barred by limitation.", "COURT_FINDING", "finding", "Current", "Court"),
    "jan-set-aside": ("8664-january", 6, "The impugned order declining the petitioner’s prayer to forward reference under Section 18 of the LA Act, is set aside.", "DISPOSITION", "disposition", "Current", "Court"),
    "jan-lac-action": ("8664-january", 6, "The LAC is directed to forward the petitioner’s reference as expeditiously as possible, preferably within a period of four weeks from today, to the concerned court.", "COURT_DIRECTION", "direction", "Current", "LAC"),
    "jan-disposition": ("8664-january", 6, "11. The petition is allowed in the aforesaid terms.", "DISPOSITION", "disposition", "Current", "Court"),
    "940-dda": ("940-may", 1, "2. Let the reply be filed by DDA within 6 weeks.", "COURT_DIRECTION", "filing", "Current", "DDA"),
    "940-registrar": ("940-may", 1, "3. List before the Joint Registrar for completion of pleadings on 28th July, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Joint Registrar"),
    "940-list": ("940-may", 1, "4. List before the Court on 28th September, 2026.", "COURT_DIRECTION", "nextHearing", "Current", "Court")}

EXAMPLES = [
    # id, task, bounded input passage IDs, selected IDs, query, language, outcome
    ("jul-extract", "semantic_proposition_extraction", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "", "English", "SUPPORTED"),
    ("may-attribute", "attribution_classification", ["may-request", "may-counter"], ["may-request", "may-counter"], "", "English", "SUPPORTED"),
    ("jan-attribute", "attribution_classification", ["jan-lac-claim", "jan-lac-correction"], ["jan-lac-claim", "jan-lac-correction"], "", "English", "SUPPORTED"),
    ("940-extract", "semantic_proposition_extraction", ["940-dda", "940-registrar", "940-list"], ["940-dda", "940-registrar", "940-list"], "", "English", "SUPPORTED"),
    ("jul-important", "important_fact_selection", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-list"], "Select the principal operative filing direction and Court listing.", "English", "SUPPORTED"),
    ("jan-important", "important_fact_selection", ["jan-lac-claim", "jan-finding", "jan-set-aside", "jan-lac-action", "jan-disposition"], ["jan-finding", "jan-set-aside", "jan-lac-action", "jan-disposition"], "Select the Court conclusions and LAC action, not an unaccepted assertion as fact.", "English", "SUPPORTED"),
    ("jan-action", "office_action_detection", ["jan-lac-action", "jan-disposition"], ["jan-lac-action"], "Which current direction is explicitly addressed to LAC?", "English", "SUPPORTED"),
    ("940-no-action", "office_action_detection", ["940-dda", "940-registrar", "940-list"], [], "Which current direction is explicitly addressed to LAC?", "English", "NONE"),
    ("jul-no-lac-action", "office_action_detection", ["jul-counter", "jul-rejoinder", "jul-list"], [], "Is an action specifically assigned to LAC, rather than generic respondents?", "English", "NONE"),
    ("jan-not-complete", "compliance_state", ["jan-lac-action", "jan-disposition"], [], "Has the Court recorded that LAC forwarded the reference?", "English", "NOT_CONFIRMED_COMPLETE"),
    ("jul-not-complete", "compliance_state", ["may-request", "may-counter", "jul-counter"], [], "Do these orders establish completed filing of the respondents' counter affidavit?", "English", "NOT_CONFIRMED_COMPLETE"),
    ("jul-date-en", "date_specific_retrieval_or_QA", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "What did the 29 July 2026 order direct?", "English", "SUPPORTED"),
    ("jul-date-hi", "date_specific_retrieval_or_QA", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "29 जुलाई 2026 के आदेश में न्यायालय ने क्या निर्देश दिए?", "Hindi", "SUPPORTED"),
    ("jul-date-hinglish", "date_specific_retrieval_or_QA", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "29 July 2026 wali hearing ke Court directions kya the?", "Hinglish", "SUPPORTED"),
    ("jul-date-roman", "date_specific_retrieval_or_QA", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "Untees July do hazaar chhabbis ke aadesh mein kya nirdesh diye?", "RomanHindi", "SUPPORTED"),
    ("jul-compensation-absent", "date_specific_retrieval_or_QA", ["jul-counter", "jul-rejoinder", "jul-list"], [], "Does the 29 July order establish that compensation was paid?", "English", "INSUFFICIENT_EVIDENCE"),
    ("jan-compliance-absent", "date_specific_retrieval_or_QA", ["jan-lac-action", "jan-disposition"], [], "Does this order establish that the reference has already been forwarded?", "English", "INSUFFICIENT_EVIDENCE"),
    ("940-digest", "order_digest", ["940-dda", "940-registrar", "940-list"], ["940-dda", "940-registrar", "940-list"], "Select a source-bound digest of this order.", "English", "SUPPORTED"),
    ("jul-digest", "order_digest", ["jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "Select a source-bound digest of this order.", "English", "SUPPORTED"),
    ("jan-digest", "order_digest", ["jan-lac-correction", "jan-finding", "jan-set-aside", "jan-lac-action", "jan-disposition"], ["jan-lac-correction", "jan-finding", "jan-set-aside", "jan-lac-action", "jan-disposition"], "Select a source-bound digest retaining the distinction between counsel and Court.", "English", "SUPPORTED"),
    ("14604-position", "multi_order_current_position", ["may-request", "may-counter", "may-registrar", "may-list", "jul-counter", "jul-rejoinder", "jul-list"], ["jul-counter", "jul-rejoinder", "jul-list"], "What is the latest recorded position across these two supplied orders?", "English", "SUPPORTED"),
    ("14604-timeline", "date_specific_retrieval_or_QA", ["may-request", "may-counter", "may-registrar", "may-list", "jul-counter", "jul-rejoinder", "jul-list"], ["may-request", "may-counter", "may-registrar", "may-list", "jul-counter", "jul-rejoinder", "jul-list"], "What is recorded in the supplied May–July 2026 orders?", "English", "SUPPORTED")]

QUARANTINED = [{"id": "940-2015-stay-uncertain", "matter_id": "W.P.(C) 940/2015",
                "state": "QUARANTINED", "reason": "Connected-matter stay attribution/source-version context not reconciled; no positive direction target permitted."}]

# Explicit review ledger: adding an EXAMPLES row alone cannot promote it to gold.
VERIFIED_IDS = {
    "jul-extract", "may-attribute", "jan-attribute", "940-extract",
    "jul-important", "jan-important", "jan-action", "940-no-action",
    "jul-no-lac-action", "jan-not-complete", "jul-not-complete",
    "jul-date-en", "jul-date-hi", "jul-date-hinglish", "jul-date-roman",
    "jul-compensation-absent", "jan-compliance-absent", "940-digest",
    "jul-digest", "jan-digest", "14604-position", "14604-timeline"}
