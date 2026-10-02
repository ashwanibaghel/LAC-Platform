"""Targeted actual-order review, not synthetic chronology or legacy rewrites.

All listed native pages were read in full. These source-level attribution
examples deliberately do not certify an unresolved historical instruction as
an active/completed current-position fact. No case-ID rules enter the gate.
"""
REVIEWED = {
 'wpc10308-2024-jul2025': ('2025-07-02', [
  (1, 'The counter- affidavit dated 27.05.2025 has been handed over in Court, and is taken on record.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Court'),
  (1, 'Ms. Avni Singh, learned counsel for GNCTD, states that she is also ready to make her submissions in the writ petition.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'GNCTD'),
  (1, '2. In view of the above, the application is allowed, and the costs imposed upon respondent Nos. 1 and 2 are waived.', 'DISPOSITION', 'disposition', 'Current', 'Court'),
  (4, '8. In the counter affidavit filed today, GNCTD has confirmed that there is a deficiency of 7 Bighas 15 Biswas in the land allotted to the petitioner/his predecessor-in-interest.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'GNCTD'),
  (4, '9. However, Ms. Singh submits that, pursuant to urbanisation, the land which was vested in the Gaon Sabha has been transferred to the Delhi Development Authority [“DDA”], and the deficiency would therefore, have to be made up by DDA.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'GNCTD'),
  (5, '11. As noted above, three opportunities have already been granted. Further time of one week is granted to DDA to file the affidavit, but the costs are enhanced to ₹20,000/-, payable to Delhi High Court Legal Services Committee', 'COURT_DIRECTION', 'filing', 'Current', 'DDA'),
  (5, '12. The affidavit will deal with the question of transfer of the deficient land by DDA to the petitioner and identification thereof. The affidavit will be filed by the Commissioner (Land Management), DDA, personally, who will also explain as to why repeated directions for filing of the affidavit have not been complied with.', 'COURT_DIRECTION', 'filing', 'Current', 'Commissioner (Land Management), DDA'),
  (5, '13. List in the category of “For Admission” matters on 11.08.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court'),
  (5, '14. The date already fixed, i.e., 28.10.2025, stands cancelled.', 'PROCEDURAL_EVENT', 'context', 'Current', 'Court')]),
 'wpc10308-2024-sep2025': ('2025-09-23', [
  (2, '3. A joint status report, as directed above, has not been filed.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court'),
  (2, 'The minutes of a meeting held on 19.09.2025 has been handed up by Ms. Manika Tripathi, learned Standing Counsel for DDA, and is taken on record.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Court'),
  (3, 'It is evident from the minutes dated 19.09.2025 that no substantive decisions were taken, except to seek more time. The matter has also not been escalated to the Vice Chairman, DDA, and to the Divisional Commissioner, GNCTD, as required by the order dated 11.08.2025.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court'),
  (3, '6. Ms. Tripathi submits that there were communications between GNCTD and DDA even prior to 08.09.2025.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'DDA'),
  (3, 'The order specifically directs that a meeting would be held at a particular level, having regard to the complexity of the issue. This answer, particularly in the absence of any record, is inadequate.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court'),
  (4, '8. In the event the concerned District Magistrate and the Commissioner (Land Management) DDA, are unable to resolve the issue within a period of two weeks from today, the issue be escalated to the Vice Chairman, DDA, and the Divisional Commissioner, GNCTD.', 'COURT_DIRECTION', 'direction', 'Current', 'DDA/GNCTD'),
  (4, '9. A joint status report be filed within six weeks from today.', 'COURT_DIRECTION', 'filing', 'Current', 'DDA/GNCTD'),
  (4, '10. List on 19.11.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc10308-2024-feb2026': ('2026-02-06', [
  (1, '2. The record has been perused. On the basis of the pleadings and the counter affidavits placed on record, there appears to be no substantial dispute as to the Petitioner’s entitlement to restoration/allotment corresponding to the deficit of land. The dispute, in essence, concerns identification and availability of land and the authority which is required to effectuate such allotment.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court'),
  (2, '4. Mr. Rajesh Yadav, senior counsel for the Petitioner, submits that the Respondents have themselves acknowledged that land admeasuring 27 Bighas 4 Biswas was taken from the Petitioner’s predecessor-in-interest, whereas only 19 Bighas 9 Biswas has been restored. It is urged that a deficit of 7 Bighas 15 Biswas thus remains to be made good.', 'PETITIONER_SUBMISSION', 'context', 'Current', 'Petitioner'),
  (2, '5. Ms. Avni Singh, panel counsel appearing for the GNCTD, submits that the matter is under consideration at the departmental level.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'GNCTD'),
  (3, '7. The competent officer of GNCTD, duly authorised and conversant with the record, shall file a short affidavit answering the above queries, along with relevant extracts of the record.', 'COURT_DIRECTION', 'filing', 'Current', 'GNCTD'),
  (3, '8. List on the date already fixed i.e., 14th April, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc5997-2024-aug2024': ('2024-08-20', [
  (1, 'Counter affidavit on behalf of respondent nos. 2 to 4 is stated to have been filed yesterday only. But the same is not on record.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'Respondents 2 to 4'),
  (1, 'Let counsel check with the Registry and get the same placed on record within a week with advance copy to counsel for the petitioner.', 'COURT_DIRECTION', 'filing', 'Current', 'Counsel for respondents 2 to 4'),
  (1, 'Rejoinder thereto be filed within four weeks as prayed for.', 'COURT_DIRECTION', 'filing', 'Current', 'Petitioner'),
  (1, 'At request, six weeks time is granted to respondent no.1 to file the counter affidavit with advance copy to counsel for the petitioner. Rejoinder thereto be filed within four weeks thereafter as prayed for.', 'COURT_DIRECTION', 'filing', 'Current', 'Respondent no.1/Petitioner'),
  (1, 'List on 09.12.2024.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc5997-2024-dec2024': ('2024-12-09', [
  (1, 'Status report stands filed by respondent no.2.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Respondent no.2'),
  (1, 'Let rejoinder thereto be filed within four weeks as prayed for.', 'COURT_DIRECTION', 'filing', 'Current', 'Petitioner'),
  (1, 'Counsel for respondent nos. 2 to 4 submits that status report filed by respondent no.2 be treated as counter affidavit on behalf of respondent nos. 2 to 4. He further submits that if required, they will file comprehensive counter affidavit.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'Respondents 2 to 4'),
  (1, 'At request, four weeks time is granted to respondent no.1 to file counter affidavit with advance copy to counsel for the petitioner. Rejoinder thereto, if any, be filed within four weeks thereafter.', 'COURT_DIRECTION', 'filing', 'Current', 'Respondent no.1/Petitioner'),
  (1, 'List on 01.05.2025.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc4806-2014-jul2026': ('2026-07-13', [
  (1, '2. This is an application for condonation of delay of 32 days in filing the additional affidavit. For the reasons stated in the application, the delay is condoned.', 'PROCEDURAL_EVENT', 'filing', 'Current', 'Court'),
  (1, '3. Let the additional affidavit be brought on record. Application is disposed of.', 'COURT_DIRECTION', 'filing', 'Current', 'Court'),
  # Preserve the official 2025 reference verbatim; do not silently "repair" it.
  (2, '6. The affidavit of the GNCTD in terms of the order dated 7th February, 2025 has not been filed.', 'COURT_OBSERVATION', 'observation', 'Current', 'Court'),
  (2, 'Mr. Sood, ld. Panel Counsel for the GNCTD submits that he would communicate the order passed today to Mr. Sameer Vashisht, ld. Standing Counsel for GNCTD.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'GNCTD'),
  (2, '7. Mr. Jha, ld. Counsel for the LAC submits that similar directions as passed on 7th February, 2026 in this matter have been issued in another matter as well being W.P.(C) 6339/2015 titled Usha Devi & Anr. vs. Union of India which is listed on 6th August, 2026.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'LAC'),
  (2, '8. The status report of the GNCTD shall positively be placed on record by the next date of hearing.', 'COURT_DIRECTION', 'filing', 'Current', 'GNCTD'),
  (2, '9. List this matter along with W.P.(C) 6339/2015 on 6th August, 2026.', 'COURT_DIRECTION', 'nextHearing', 'Current', 'Court')]),
 'wpc15198-2025': ('2026-02-13', [
  (2, '5. Ld. Counsel for the Petitioner submits that under Section 3G(5) of the National Highways Act, 1956, the Petitioner’s claim is liable to be referred to arbitration to the Special Secretary, as notified by the Government.', 'PETITIONER_SUBMISSION', 'context', 'Current', 'Petitioner'),
  (4, '7. Mr. Pathak, ld. Counsel for the LAC submits that a communication has been written by the Petitioner directly to the Special Secretary by the Petitioner, who is not a Competent Authority since the Special Secretary has to himself act as the Arbitrator.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'LAC'),
  (15, '12. In view thereof, the Respondent No. 2 - Competent Authority (Land Acquisition)/CALA shall make the reference to arbitration within a period of 30 days in accordance with law.', 'COURT_DIRECTION', 'referenceToAdj', 'Current', 'CALA'),
  (15, '13. A copy of this order be communicated to Respondent No. 2 - Competent Authority (Land Acquisition)/CALA formally by the Petitioner within a period of one week. The reference shall be made by the said Respondent No. 2 within 30 days thereafter.', 'COURT_DIRECTION', 'direction', 'Current', 'Petitioner/CALA'),
  (15, '14. The Court has been assured by Mr. Pathak, ld. Counsel for the LAC that the post of the Special Secretary, who has to act as an Arbitrator has now been filled up.', 'LAC_OR_RESPONDENT_SUBMISSION', 'context', 'Current', 'LAC'),
  (15, '16. The petition is disposed of in the above terms. Pending applications, if any, are also disposed of.', 'DISPOSITION', 'disposition', 'Current', 'Court')]),
}

PASSAGES = {f'{key}-chain-p{i}': (key, *p) for key, (_, ps) in REVIEWED.items() for i, p in enumerate(ps)}
OPEN = set()
STRONG = {}  # No full-chain certification from silence about earlier duties.
EXAMPLES = []
for key, (_, passages) in REVIEWED.items():
 # Disjoint substantive chunks, not paraphrases of the same attribution task.
 for start in range(0, len(passages), 5):
  ids = [f'{key}-chain-p{i}' for i in range(start, min(start + 5, len(passages)))]
  EXAMPLES.append((f'v3-{key}-native-attribution-{start}', 'attribution_classification', ids, ids, '', 'English', 'SUPPORTED'))

EXAMPLES.extend([
 ('v3-10308-september-no-joint-report', 'compliance_state',
  ['wpc10308-2024-sep2025-chain-p0', 'wpc10308-2024-sep2025-chain-p1', 'wpc10308-2024-sep2025-chain-p2'], [],
  'Does the Court record performance of the ordered joint status report or escalation? Distinguish the meeting minutes actually taken on record from the report and escalation not performed.', 'English', 'NOT_CONFIRMED_COMPLETE'),
 ('v3-5997-reported-counter-not-confirmed', 'compliance_state',
  ['wpc5997-2024-aug2024-chain-p0', 'wpc5997-2024-aug2024-chain-p1'], [],
  'Is the respondent counter affidavit confirmed on record, or merely stated to be filed with further Registry steps directed?', 'English', 'NOT_CONFIRMED_COMPLETE'),
])
