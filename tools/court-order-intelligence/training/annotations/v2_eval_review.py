"""Fresh protected source/task review. NEVER import into V2 training corpus.

Authored from complete native sources before model inference. No prediction or
training-error output is used. Freeze this file and exported sets before GPU.
Cross-page incomplete propositions are not repaired into unsupported evidence.
"""
REVIEW = {'reviewer': 'Codex source-review pass (not human legal certification)',
          'method': 'Full native-source review before any V2 output; exact bounded passages and separate protected targets',
          'reviewed_on': '2026-10-02', 'annotation_version': 'pilot-v2-protected-review-1'}

REVIEWED = {
 'wpc6504-2023-oct2024': ('2024-10-23', [
  (1, '1. The record would reflect that despite a final opportunity having been granted, the respondents have failed to file a reply. Their right to do so consequently stands foreclosed.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Respondents', 'Failure and closed reply right; not a new reply obligation or completed compliance.'),
  (1, '2. However, and since a doubt is expressed with respect to the Khasra numbers which are mentioned in the relief clause of the writ petition and whether the same formed part of the original notification under Section 6 of the Land Acquisition Act, 1894 and the Award No. 184/86-87, we permit Mr. Khan, learned counsel who appears for the Land and Acquisition Collector, to place a short Status Report on our record before we finally dispose of the writ petition.', 'COURT_DIRECTION', 'filing', 'Current', 'LAC', 'Specific LAC short-report permission is distinct from foreclosed reply; future disposition not actual disposal.'),
  (1, '3. Let the said Report be placed on our record within a period of four weeks from today.', 'COURT_DIRECTION', 'filing', 'Current', 'LAC', 'Express report requirement with preceding LAC actor; no completion assumed.'),
  (1, '4. Let the writ petition be re-notified for 04.12.2024.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'This order listing only.')]),
 'wpc6504-2023-may2026': ('2026-05-07', [
  (1, '2. Vide order dated 23rd October, 2024, the right of the LAC to file a reply was closed due to their failure to do so, despite repeated opportunities. However, LAC was permitted to file a short status report.', 'CASE_CONTEXT', 'context', 'Historical', 'LAC', 'Recalls earlier closure/report permission; not a fresh reply direction.'),
  (1, '3. The stand of the Petitioner is that the entire land has been de-notified and it is only an error by which the Petitioner’s land is not mentioned in the denotification.', 'PETITIONER_SUBMISSION', 'context', 'Current', 'Petitioner', 'De-notification claim is not a judicial finding.'),
  (1, '4. This is a short issue and the LAC ought to have filed a short status report in this regard.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court', 'Court observes missing expected report, not compliance.'),
  (1, '5. Mr. Jha, ld. Counsel submits that Mr. Khan, ld. Counsel who was previously handling the matter is no longer on the panel.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'LAC', 'Counsel explanation does not establish compliance.'),
  (2, '7. List on 3rd July, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Listing is not an invented future order.')]),
 'wpc6504-2023': ('2026-07-03', [
  (1, 'Mr. Pathak, ld. Counsel for the LAC has not filed the status report', 'COURT_OBSERVATION', 'observation', 'Current', 'Court', 'Latest Court records absent report; do not infer expired period equals compliance.'),
  (1, 'As per the said communication, the de-notification of these Khasra numbers was rejected as the possession of the land has already been taken by the Delhi Development Authority (hereinafter, ‘DDA’).', 'LAC_OR_RESPONDENT_SUBMISSION', 'possession', 'Current', 'LAC', 'Communication contents stay attributed; not an independent Court possession finding.'),
  (1, 'In respect of the remaining Khasra numbers, the exact status is not clear.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court', 'Uncertainty explicitly retained.'),
  (2, 'Accordingly, let the Secretary, Land and Development Office and LAC place on record the status of the digitization of the land acquisition records and other relevant files by the next date of hearing. In addition if there are any impediments being faced in digitization, the same shall also be spelt out in the status report.', 'COURT_DIRECTION', 'filing', 'Current', 'LAC', 'Explicit jointly named digitization report, not a new unconditional possession determination.'),
  (2, 'Ld. Counsel for the DDA to file an affidavit on behalf of the DDA in this matter by the next date of hearing.', 'COURT_DIRECTION', 'filing', 'Current', 'DDA', 'DDA duty is not assigned to LAC.'),
  (2, '9. List on 7th September, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Latest supplied Court date.')]),
 'wpc5993-2024': ('2026-05-29', [
  (2, '3. The cases of 28th May, 2026 and 29th May, 2026 have been listed together today and due to heavy board, these matters cannot be taken up for final hearing.', 'PROCEDURAL_EVENT', 'context', 'Current', 'Court', 'No substantive final hearing or merits finding invented.'),
  (2, '4. List on 11th September, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Only listing direction.')]),
 'wpc16215-2023': ('2026-08-21', [
  (1, '1. Counsel for the Petitioner seeks as adjournment as Mr. Rakesh Kumar, counsel who is to argue the matter for the Petitioner, is not available today.', 'PETITIONER_SUBMISSION', 'context', 'Current', 'Petitioner', 'Adjournment request is not an LAC obligation.'),
  (1, '2. At request, re-notify on 25th January, 2027.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Date only; no merits finding.')]),
 'wpc3238-2023': ('2025-11-10', [
  (1, '1. No time left.', 'PROCEDURAL_EVENT', 'context', 'Current', 'Court', 'No substantive hearing result inferred.'),
  (1, '2. List on 19th February, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Future listed date is not proof of an order.')]),
 'wpc847-2025': ('2026-03-28', [
  (1, '1. There is no representation on behalf of respondent no.1-the Deputy Commissioner/ District Magistrate, South West District, Kapashera, New Delhi and respondent no.2- Land and Building Department, Government of NCT of Delhi.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court', 'Named nonrepresentation, not a finding of contempt or personal LAC duty.'),
  (1, '3. List on 14.05.2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Listing only.'),
  (2, '5. Interim order to continue.', 'COURT_DIRECTION', 'direction', 'Current', 'Court', 'Continue existing interim order, scope not expanded.')]),
 'wpc3282-2024': ('2026-02-12', [
  (1, 'List on 07.09.2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Listing only, no substantive finding.')]),
 'wpc3282-2024-sep2026': ('2026-09-07', [
  (1, 'List on 19.02.2027.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Latest supplied listing replaces earlier listing, not proven future order.')]),
 'wpc10105-2023-dec2024': ('2024-12-06', [
  (1, 'Short reply by respondent nos. 1 to 3 stands filed.', 'DOCUMENT_OR_FILING_FACT', 'filing', 'Current', 'Respondents 1 to 3', 'Confirmed short reply is not detailed counter-affidavit completion.'),
  (1, 'Let counter affidavit by respondent no.4 and detailed counter affidavit by respondent nos. 1 to 3 be filed within four weeks with advance copy to counsel for the petitioner.', 'COURT_DIRECTION', 'filing', 'Current', 'Respondents 1 to 4', 'Generic numbered respondents not an explicitly individual LAC-only obligation.'),
  (1, 'List on 15.05.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Actual source listing only.')]),
 'wpc10105-2023-may2025': ('2025-05-15', [
  (1, 'Short reply stands filed by respondent nos. 1 to 3.', 'DOCUMENT_OR_FILING_FACT', 'filing', 'Current', 'Respondents 1 to 3', 'Short reply remains distinct from required counter affidavits.'),
  (1, 'At request, four weeks time is granted to respondent nos. 1 to 4 to file counter affidavits failing which, right to file the same shall automatically stand closed.', 'COURT_DIRECTION', 'filing', 'Current', 'Respondents 1 to 4', 'Conditional future closure is not already-closed right or proven filing.'),
  (1, 'List on 07.11.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Source-backed listing.')]),
 'wpc10105-2023-jul2025': ('2025-07-11', [
  (1, 'CM APPL. 40492/2025(For Exemption) 1. Exemption allowed, subject to all just exceptions. 2. Application stands disposed of.', 'PROCEDURAL_EVENT', 'disposition', 'Current', 'Court', 'Named exemption application only, not writ disposal.'),
  (2, 'He has further argued that since no compensation in respect of the subject land was made to the petitioner as such, the acquisition itself is vitiated.', 'PETITIONER_SUBMISSION', 'compensation', 'Current', 'Petitioner', 'Nonpayment/vitiation argument not judicial established finding.'),
  (2, '3. Further submission of the learned counsel for petitioner is that in fact the Section 4 and Section 6 notifications under Land Acquisition Act 1894, were issued on 04.11.2004 and 31.10.2005 respectively, and an award was also declared under Section 11of the Land Acquisition Act 1894,on 06.07.2008, however since then no possession has yet been taken.', 'PETITIONER_SUBMISSION', 'possession', 'Current', 'Petitioner', 'Historical dates in submitted possession assertion do not establish possession fact.'),
  (4, '11. It has been argued on the other hand by learned counsel representing the respondent that payment of compensation could not be made earlier on account of various litigations.', 'LAC_OR_RESPONDENT_SUBMISSION', 'compensation', 'Current', 'Respondent', 'Respondent explanation retains attribution, not unqualified payment/nonpayment finding.'),
  (4, 'That is the issue to be decided finally in the Writ Petition.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court', 'Court leaves issue unresolved, not finally decided.'),
  (4, 'what we find is that at this juncture, restraining the respondents/State from taking possession of the land will not be appropriate.', 'COURT_FINDING', 'possession', 'Current', 'Court', 'Interim restraint declined, not actual completed taking of possession.'),
  (5, '16. The application stands disposed of.', 'PROCEDURAL_EVENT', 'disposition', 'Current', 'Court', 'Civil miscellaneous application only; full reviewed source still lists the writ.'),
  (5, '1. List on 07.11.2025 before Joint Registrar.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Joint Registrar listing distinct from a merits hearing.')]),
 'wpc10105-2023': ('2026-08-31', [
  (3, '2. The Hon’ble Division Bench could not assemble today as Hon’ble Ms. Justice Prathiba M. Singh and Hon’ble Mr. Justice Vikas Mahajan are on leave.', 'PROCEDURAL_EVENT', 'context', 'Current', 'Court', 'Latest nonassembly is not a merits resolution.'),
  (3, '3. List on 17th September, 2026 on top of board.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court', 'Latest supplied listing, not proof of an order on that date.')]),
}

QUARANTINED = [
 {'source': 'wpc6504-2023-may2026', 'task': 'office_action_detection',
  'reason': 'Renewed short-report deadline crosses page boundary; one-page proposition contract cannot retain complete sentence. No misleading NONE example exported.'},
 {'source': 'wpc847-2025', 'task': 'personal-appearance-direction',
  'reason': 'Dispensation condition crosses page boundary; do not export unconditional personal-appearance duty.'},
 {'source': 'wpc10105-2023-jul2025', 'task': 'possession-outcome-direction',
  'reason': 'Outcome-of-writ qualification crosses pages 4/5; do not invent a one-page unconditional possession directive.'},
]
PASSAGES = {f'{version}-e{i}': (version, *p[:-1])
            for version, (_, ps) in REVIEWED.items() for i, p in enumerate(ps)}
EXAMPLES = []
for version, (date, passages) in REVIEWED.items():
    ids = [f'{version}-e{i}' for i in range(len(passages))]
    for start in range(0, len(ids), 6):
        chunk = ids[start:start+6]
        EXAMPLES.append((version + f'-eval-attribute-{start}', 'attribution_classification', chunk, chunk, '', 'English', 'SUPPORTED'))
    # No digest for sources with excluded cross-page operative qualifications.
    # Bounded factual QA is not represented as an exhaustive order summary.
    EXAMPLES.append((version + '-eval-date', 'date_specific_retrieval_or_QA', ids, ids,
                     f'{date} ke supplied passages kya record karte hain? Yeh complete order history ka claim nahi hai.', 'RomanHindi', 'SUPPORTED'))
    if version != 'wpc6504-2023-may2026':
        selected = [pid for pid in ids if PASSAGES[pid][3] == 'COURT_DIRECTION' and PASSAGES[pid][6] == 'LAC']
        EXAMPLES.append((version + '-eval-action', 'office_action_detection', ids, selected,
                         'What obligation is expressly assigned to LAC itself by these supplied passages? Keep other actors separate.', 'English', 'SUPPORTED' if selected else 'NONE'))
    EXAMPLES.append((version + '-eval-no-compliance', 'compliance_state', ids, [],
                     'Do these passages confirm completion of every earlier LAC filing/report duty? A short reply is not a detailed counter affidavit.', 'English', 'NOT_CONFIRMED_COMPLETE'))
    EXAMPLES.append((version + '-eval-unsupported', 'date_specific_retrieval_or_QA', ids, [],
                     'Kya in supplied passages se sabhi compensation ka payment aur har duty ki completion proven hai?', 'Hinglish', 'INSUFFICIENT_EVIDENCE'))

# Current-position gold uses real source chains and is NEVER training input.
CHAIN_DECISIONS = {
 '6504-eval-position': (
  ['wpc6504-2023-oct2024-e0', 'wpc6504-2023-oct2024-e1', 'wpc6504-2023-may2026-e1',
   'wpc6504-2023-e0', 'wpc6504-2023-e1', 'wpc6504-2023-e2', 'wpc6504-2023-e3', 'wpc6504-2023-e5'],
  ['wpc6504-2023-oct2024-e0', 'wpc6504-2023-may2026-e1', 'wpc6504-2023-e0',
   'wpc6504-2023-e1', 'wpc6504-2023-e2', 'wpc6504-2023-e3', 'wpc6504-2023-e5'],
  'Across three supplied orders, what remains unresolved and what is the latest LAC position/action and Court listing? Keep denotification/possession stands attributed; no report completion assumed.', 'English'),
 '6504-eval-noncompletion': (
  ['wpc6504-2023-oct2024-e1', 'wpc6504-2023-may2026-e2', 'wpc6504-2023-e0'],
  ['wpc6504-2023-e0'], 'Teen orders ke baad report ki latest actual filing position kya hai? Expired period ko completed report mat samjho.', 'RomanHindi'),
 '10105-eval-position': (
  ['wpc10105-2023-dec2024-e0', 'wpc10105-2023-may2025-e1',
   'wpc10105-2023-jul2025-e1', 'wpc10105-2023-jul2025-e3', 'wpc10105-2023-jul2025-e4',
   'wpc10105-2023-jul2025-e5', 'wpc10105-2023-jul2025-e6', 'wpc10105-2023-e1'],
  ['wpc10105-2023-jul2025-e1', 'wpc10105-2023-jul2025-e3', 'wpc10105-2023-jul2025-e4',
   'wpc10105-2023-jul2025-e5', 'wpc10105-2023-jul2025-e6', 'wpc10105-2023-e1'],
  'Is case ki supplied four-order chain ka current scene batao. Compensation ke rival stands, unresolved writ, interim decision aur latest listing alag rakho. August nonassembly ko merits decision na banao.', 'Hinglish'),
 '10105-eval-compensation': (
  ['wpc10105-2023-dec2024-e0', 'wpc10105-2023-may2025-e0',
   'wpc10105-2023-jul2025-e1', 'wpc10105-2023-jul2025-e3', 'wpc10105-2023-jul2025-e4'],
  ['wpc10105-2023-jul2025-e1', 'wpc10105-2023-jul2025-e3', 'wpc10105-2023-jul2025-e4'],
  'तीन आदेशों में compensation की क्या स्थिति स्थापित है? Petitioner की nonpayment दलील, respondent का explanation और Court का unresolved issue अलग रखें।', 'Hindi'),
 '10105-eval-filing-position': (
  ['wpc10105-2023-dec2024-e0', 'wpc10105-2023-dec2024-e1',
   'wpc10105-2023-may2025-e0', 'wpc10105-2023-may2025-e1', 'wpc10105-2023-e0'],
  ['wpc10105-2023-may2025-e0', 'wpc10105-2023-may2025-e1'],
  'From three actual supplied orders, what filing is confirmed and what remained an extended requirement? Later nonassembly does not prove counter affidavits filed.', 'English'),
}
for key, (supplied, selected, question, language) in CHAIN_DECISIONS.items():
    EXAMPLES.append((key, 'multi_order_current_position', supplied, selected, question, language, 'SUPPORTED'))
VERIFIED_IDS = frozenset(e[0] for e in EXAMPLES)
