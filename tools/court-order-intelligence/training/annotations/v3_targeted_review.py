"""V3-only targeted native review after legacy recalculation, 2026-10-03.

No legacy target is changed. Seven additional PDFs were fully read; most were
procedural, not counted as rich synthesis. Only explicit reviewed passages
below are gold candidates. Acquired sources remain locally ignored.
"""
REVIEWED = {
 'wpc5202-2024-apr2025': ('2025-04-15', [
  (2, '1. The Hon’ble Division Bench has not assembled since Hon’ble the Chief Justice is on leave.', 'PROCEDURAL_EVENT', 'context', 'Current', 'Court'),
  (2, '2. List on 12.08.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc5202-2024-nov2025': ('2025-11-04', [
  (1, '1. Learned counsel appearing for respondent nos.2 & 3 seeks time of two weeks to file response.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'Respondents 2 and 3'),
  (1, '2. By way of last chance, list on 09th January, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc5202-2024': ('2026-01-09', [
  (2, '5. In W.P.(C) 5202/2024, the brief facts of the case are that the Petitioners had filed a reference under Section 18 of the Act before the LAC, South West District, Kapashera, New Delhi, on 29th March, 2011, seeking enhancement of compensation granted to them under the Award passed on 26th December, 2008.However, no action has been taken in this regard by the LAC. Thus, the Petitioners are seeking, inter alia, direction to the concerned LAC to consider the said reference filed by them and refer the same for judicial considerationto the District Judge.', 'CASE_CONTEXT', 'context', 'Current', 'Court'),
  (3, 'In the counter affidavits filed, the stand taken by the LAC is that the reference applications were filed beyond the time prescribed under Section 18(2) of the Act.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'LAC'),
  (6, '10. Under such circumstances, this Court is of the opinion that the matters ought to be referred to the District Judge for decision of the references made by the Petitioners under Section 18 of the Act, rather than keeping the entire issue pending before the LAC itself.', 'COURT_FINDING', 'finding', 'Current', 'Court'),
  (6, '11. The concerned District Judge, who would receive the said references under Section 18 of the Act would also, among other issues, look into the matter and come to a proper conclusion as to whether the said references were filed under Section 18 of the Act within the stipulated time limit or not.', 'COURT_OBSERVATION', 'observation', 'Current', 'District Judge'),
  (6, '12. Under these circumstances, the LAC is directed to forward the references filed by the Petitioners in both these matters under Section 18 of the Act to the concerned District Judge for the purpose of determination of the market value of the following land:', 'COURT_DIRECTION', 'referenceToAdj', 'Current', 'LAC'),
  (7, '14. The original copy of the references filed by the Petitioners under Section 18 of the Act, bearing the seal of the LAC, as available with the Petitioners shall also be filed or produced before the concerned District Judge, as may be directed by the said Court.', 'COURT_DIRECTION', 'filing', 'Current', 'Petitioners'),
  (7, '15. The petitions are disposed of in these terms. Pending applications, if any, are also disposed of.', 'DISPOSITION', 'disposition', 'Current', 'Court')]),
 'wpc10308-2024-aug2025': ('2025-08-11', [
  (1, '1. Delhi Development Authority [“DDA”] has filed an affidavit in compliance with paragraphs 11 and 12 of the order dated 02.07.2025.', 'RECORDED_COMPLIANCE', 'compliance', 'Current', 'DDA'),
  (1, 'A copy of the affidavit has been handed up in Court and is taken on record.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Court'),
  (2, '4. For the present it is directed that the Commissioner, Land Management, DDA and the concerned District Magistrate will hold a meeting and attempt to resolve the issue, failing which the issue will be escalated to the Vice Chairman, DDA, and the Divisional Commissioner, GNCTD.', 'COURT_DIRECTION', 'direction', 'Current', 'DDA/GNCTD'),
  (2, 'A joint status report be filed within four weeks from today.', 'COURT_DIRECTION', 'filing', 'Current', 'DDA/GNCTD'),
  (2, '5. List on 23.09.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
}

PASSAGES = {f'{key}-v3-p{i}': (key, *p) for key, (_, ps) in REVIEWED.items() for i, p in enumerate(ps)}
# OPEN is an explicit instruction in this as-issued snapshot, not proof of
# current live compliance or a revival of an old direction in a later chain.
OPEN = {'wpc5202-2024-v3-p3', 'wpc5202-2024-v3-p4', 'wpc5202-2024-v3-p5',
        'wpc10308-2024-aug2025-v3-p2', 'wpc10308-2024-aug2025-v3-p3'}
STRONG = {'v3-5202-full-position': 'Three actual PDFs: prior non-assembly and counsel extension, latest Section 18 dispute, LAC forwarding duty, District Judge timeliness issue, conditional originals production and disposal; no referral-completed or timely-filing conclusion is invented.'}

EXAMPLES = [
 ('v3-5202-full-position', 'multi_order_current_position',
  ['wpc5202-2024-apr2025-v3-p1', 'wpc5202-2024-nov2025-v3-p0', *[f'wpc5202-2024-v3-p{i}' for i in (0,1,3,4,5,6)]],
  [f'wpc5202-2024-v3-p{i}' for i in (0,1,3,4,5,6)],
  'Across these three actual supplied orders, what is the latest position? Distinguish the earlier extension/listing, disputed Section 18 timeliness, LAC referral duty, District Judge inquiry, conditional originals production and disposal. Do not imply reference forwarding was completed.', 'English', 'SUPPORTED'),
 ('v3-5202-lac-referral', 'office_action_detection', [f'wpc5202-2024-v3-p{i}' for i in (1,3,4,5,6)], ['wpc5202-2024-v3-p4'], 'What did this order require LAC itself to do? Distinguish the District Judge inquiry and Petitioners’ conditional original-document duty.', 'English', 'SUPPORTED'),
 ('v3-5202-no-referral-completion', 'compliance_state', [f'wpc5202-2024-v3-p{i}' for i in (3,4,5,6)], [], 'Do these passages record that LAC actually forwarded the Section 18 reference, rather than requiring forwarding?', 'English', 'NOT_CONFIRMED_COMPLETE'),
 ('v3-10308-dda-affidavit-performance', 'compliance_state', ['wpc10308-2024-aug2025-v3-p0', 'wpc10308-2024-aug2025-v3-p1', 'wpc10308-2024-aug2025-v3-p3'], ['wpc10308-2024-aug2025-v3-p0'], 'Which filing does the Court record as performed, for which actor? Do not turn the new joint-report requirement into performance or claim the disputed land was restored.', 'English', 'SUPPORTED'),
]
for key, (_, passages) in REVIEWED.items():
 for start in range(0, len(passages), 6):
  ids = [f'{key}-v3-p{i}' for i in range(start, min(start + 6, len(passages)))]
  EXAMPLES.append((f'v3-{key}-attribution-{start}', 'attribution_classification', ids, ids, '', 'English', 'SUPPORTED'))
