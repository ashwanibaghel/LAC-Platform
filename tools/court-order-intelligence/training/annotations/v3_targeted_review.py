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

# Complete rereviews of existing TRAIN sources. These are NEW V3 targets, never
# replacements of the narrower frozen legacy targets. All native pages were
# reread, including separate non-LAC duties and conditional costs.
from annotations.v2_review import REVIEWED as _v2_review
for _key in ('wpc4806-2014-feb2020', 'wpc4806-2014-apr2022', 'wpc4806-2014',
             'wpc13932-2025-sep2025', 'wpc13932-2025-feb03', 'wpc13932-2025'):
 _day, _ps = _v2_review[_key]
 REVIEWED[_key] = (_day, [p[:6] for p in _ps])
REVIEWED['wpc4806-2014'][1].extend([
 (2, '8. Accordingly, let the Secretary, Services, Government of National Capital Territory of Delhi (hereinafter, ‘GNCTD’) look into the matter relating to short staffing at the LAC in terms of the sanctioned posts and the presently working staff. Let a status report be filed by the Secretary, Services, GNCTD in respect of the following by the next date of hearing: i. Total sanction strength of the LAC in all Districts in Delhi; ii. Total number of officials presently working in the LAC office; iii. Steps taken to fill up the vacancies, if any; iv. Proposal to immediately fill up the vacant positions in the LAC offices in the various districts.', 'COURT_DIRECTION', 'filing', 'Current', 'Secretary, Services, GNCTD'),
 (2, '9. Copy of this order be served upon Mr. Sameer Vashisht, ld. Standing Counsel for GNCTD by the Registry.', 'COURT_DIRECTION', 'direction', 'Current', 'Registry')])
REVIEWED['wpc13932-2025'][1].extend([
 (2, 'Let all the counter affidavits be brought on record.', 'COURT_DIRECTION', 'filing', 'Current', 'Respondents'),
 (2, '8. Let rejoinders be filed within six weeks.', 'COURT_DIRECTION', 'filing', 'Current', 'Petitioners')])
_new_keys = ('wpc4806-2014-feb2020', 'wpc4806-2014-apr2022', 'wpc4806-2014',
             'wpc13932-2025-sep2025', 'wpc13932-2025-feb03', 'wpc13932-2025')
for _key in _new_keys:
 PASSAGES.update({f'{_key}-v3-p{i}': (_key, *p) for i, p in enumerate(REVIEWED[_key][1])})
OPEN.update({'wpc4806-2014-v3-p2', 'wpc4806-2014-v3-p3', 'wpc4806-2014-v3-p4',
             'wpc4806-2014-v3-p5', 'wpc4806-2014-v3-p6', 'wpc13932-2025-v3-p4',
             'wpc13932-2025-v3-p5', 'wpc13932-2025-v3-p6', 'wpc13932-2025-v3-p7'})
STRONG.update({
 'v3-4806-complete-feb2026': 'Three actual orders: counsel correction assurance, last three-week opportunity, latest judicial non-correction, renewed four-week LAC affidavit and conditional costs, separate Secretary staffing/report and Registry service duties, latest listing. No deadline expiry used as completion.',
 'v3-13932-complete-aug2026': 'Three actual orders: disputed title/release and historical acquisition, completed original-owner service, later impleadment and application-only disposal, stated counters vs judicial condonation, outstanding counter-record/rejoinder/amended-memo requirements and latest Court date. No writ disposal or confirmed counter filing inferred.'})
EXAMPLES.extend([
 ('v3-4806-complete-feb2026', 'multi_order_current_position',
  ['wpc4806-2014-feb2020-v3-p0', 'wpc4806-2014-apr2022-v3-p0', *[f'wpc4806-2014-v3-p{i}' for i in range(7)]],
  [f'wpc4806-2014-v3-p{i}' for i in (0,1,2,3,4,5,6)],
  'Give the complete current position as of the latest of these three actual orders. Distinguish the earlier filing assurance/extension, present uncorrected errors, renewed LAC affidavit with conditional costs, Secretary Services staffing report and Registry service obligations, and latest Court listing. Do not assign every actor’s work to LAC.', 'English', 'SUPPORTED'),
 ('v3-13932-complete-aug2026', 'multi_order_current_position',
  [*[f'wpc13932-2025-sep2025-v3-p{i}' for i in (0,1,2)], 'wpc13932-2025-feb03-v3-p1', *[f'wpc13932-2025-v3-p{i}' for i in range(8)]],
  [*[f'wpc13932-2025-sep2025-v3-p{i}' for i in (0,1,2)], 'wpc13932-2025-feb03-v3-p1', *[f'wpc13932-2025-v3-p{i}' for i in range(8)]],
  'What is the full current position in this supplied three-order chain? Keep the claimed title/release distinct from the historical Award. Include actual service and later impleadment, limited application disposal, stated counter filing versus condonation and bringing counters on record, petitioner rejoinder/amended memo duties, and latest hearing. Do not decide title or dispose of the writ.', 'English', 'SUPPORTED'),
])
# Only new attribution chunks for newly reviewed passages; do not pad counts by
# re-exporting the earlier identical selections under different IDs/questions.
for _key, _start in (('wpc4806-2014', 5), ('wpc13932-2025', 6)):
 _ids = [f'{_key}-v3-p{i}' for i in range(_start, len(REVIEWED[_key][1]))]
 EXAMPLES.append((f'v3-{_key}-additional-actors', 'attribution_classification', _ids, _ids, '', 'English', 'SUPPORTED'))
