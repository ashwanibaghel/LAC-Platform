"""Explicit V2 task decisions over source-reviewed passages, never model output.

This training foundation remains partial until protected gold/splits/context
audits are complete. Supplied chains are not exhaustive Court histories.
"""
from annotations.v2_review import REVIEWED

REVIEW = {'reviewer': 'Codex source-review pass (not human legal certification)',
          'method': 'Full native-order read; exact page/passage binding; independently authored targets',
          'reviewed_on': '2026-10-02', 'annotation_version': 'pilot-v2-source-task-review-2'}

PASSAGES = {f'{version}-p{i}': (version, *passage[:-1])
            for version, (_, passages) in REVIEWED.items()
            for i, passage in enumerate(passages)}

# Explicit per-order positive IDs. Every other reviewed order is NONE for an
# individually and expressly assigned LAC action. Generic respondents excluded.
ACTION_INDICES = {
    'wpc6108-2015-nov2025': [0, 1],
    'wpc6108-2015-feb25': [3, 4],
    'wpc4806-2014-feb2020': [1],
    'wpc4806-2014-apr2022': [1],
    'wpc4806-2014': [2, 3],
    'wpc6108-2015': [1],
    'wpc4255-2016': [1],
    'wpc9093-2022': [2, 3, 4],
}
COMPLIANCE_INDICES = {'wpc6108-2015-feb25': [1], 'wpc13932-2025-feb03': [1]}

EXAMPLES = []
for version, (date, reviewed) in REVIEWED.items():
    ids = [f'{version}-p{i}' for i in range(len(reviewed))]
    actions = [ids[i] for i in ACTION_INDICES.get(version, [])]
    completion = [ids[i] for i in COMPLIANCE_INDICES.get(version, [])]
    # At most six anchors per extraction contract. Failed frozen runtime
    # expansion quarantines the WHOLE chunk; do not silently remove a label.
    for start in range(0, len(ids), 6):
        chunk = ids[start:start + 6]
        EXAMPLES.append((f'{version}-attribute-{start}', 'attribution_classification',
                         chunk, chunk, '', 'English', 'SUPPORTED'))
    definitions = [
        ('action', 'office_action_detection', actions,
         'Is order mein LAC ko khud kya karna hai? Sirf expressly assigned current directions; generic respondents aur doosre officers alag rakho.',
         'Hinglish', 'SUPPORTED' if actions else 'NONE'),
        ('compliance', 'compliance_state', completion,
         'What performance does the Court actually record as complete, and for which actor? A promise, time extension or expired deadline is not completion.',
         'English', 'SUPPORTED' if completion else 'NOT_CONFIRMED_COMPLETE'),
        ('digest', 'order_digest', ids,
         'इस उपलब्ध आदेश का सार दें। पक्ष का दावा, न्यायालय का निष्कर्ष और निर्देश अलग रखें; शर्तें न हटाएं।',
         'Hindi', 'SUPPORTED'),
        ('date', 'date_specific_retrieval_or_QA', ids,
         f'{date} ke supplied order mein asal mein kya hua? Iski listing date ko ek aur order mat samjho.',
         'RomanHindi', 'SUPPORTED'),
        ('unsupported-completion', 'date_specific_retrieval_or_QA', [],
         'Do these passages establish that every LAC obligation has been fulfilled and all compensation paid?',
         'English', 'INSUFFICIENT_EVIDENCE'),
    ]
    for suffix, task, selected, question, language, outcome in definitions:
        EXAMPLES.append((f'{version}-{suffix}', task, ids, selected, question, language, outcome))

# Bounded chronological chains: every tuple is supplied IDs, selected IDs,
# query, task, language, outcome, source-review reasoning.
CHAIN_DECISIONS = {
 '9093-position': (
  ['wpc9093-2022-apr2024-p0', 'wpc9093-2022-apr2024-p1',
   'wpc9093-2022-feb09-p0', 'wpc9093-2022-feb09-p1',
   'wpc9093-2022-p2', 'wpc9093-2022-p3', 'wpc9093-2022-p4', 'wpc9093-2022-p6'],
  ['wpc9093-2022-apr2024-p0', 'wpc9093-2022-apr2024-p1',
   'wpc9093-2022-feb09-p0', 'wpc9093-2022-p2', 'wpc9093-2022-p3',
   'wpc9093-2022-p4', 'wpc9093-2022-p6'],
  'तीन उपलब्ध आदेशों से विवाद और वर्तमान स्थिति बताएं। कब्जे और compensation की बात किसका दावा है? Latest LAC meeting/records duty और Court listing अलग रखें; meeting date को hearing न कहें।',
  'multi_order_current_position', 'Hindi', 'SUPPORTED',
  'Original possession/compensation demand and SDM report stay attributed; latest joint-meeting records duty is explicit, not proof of handover or compensation.'),
 '9093-active-action': (
  ['wpc9093-2022-apr2024-p1', 'wpc9093-2022-feb09-p2',
   'wpc9093-2022-p2', 'wpc9093-2022-p3', 'wpc9093-2022-p4', 'wpc9093-2022-p5'],
  ['wpc9093-2022-p2', 'wpc9093-2022-p3', 'wpc9093-2022-p4'],
  'Across the three supplied orders, what must LAC itself now do? Do not assign DPIIT’s status report to LAC or turn petitioner requests into granted relief.',
  'office_action_detection', 'English', 'SUPPORTED',
  'Current expressly named LAC presence and record production; DPIIT report is a different actor’s duty.'),
 '9093-compensation': (
  ['wpc9093-2022-apr2024-p0', 'wpc9093-2022-apr2024-p1',
   'wpc9093-2022-feb09-p1', 'wpc9093-2022-p4'],
  ['wpc9093-2022-apr2024-p1'],
  'Compensation ke bare mein supplied orders kya establish karte hain? Petitioner ki demand batao, payment ya Court award invent mat karo.',
  'date_specific_retrieval_or_QA', 'RomanHindi', 'SUPPORTED',
  'Only petitioner entitlement claim is established as a claim; no payment or compensation award is proven.'),
 '4806-position': (
  ['wpc4806-2014-feb2020-p0', 'wpc4806-2014-feb2020-p1',
   'wpc4806-2014-apr2022-p0', 'wpc4806-2014-apr2022-p1',
   'wpc4806-2014-p0', 'wpc4806-2014-p1', 'wpc4806-2014-p2', 'wpc4806-2014-p4'],
  ['wpc4806-2014-p0', 'wpc4806-2014-p1', 'wpc4806-2014-p2', 'wpc4806-2014-p4'],
  'Across these three supplied orders, what is the latest recorded position, still-unconfirmed corrected affidavit, renewed LAC duty and latest listed Court date? Do not treat earlier deadlines/listings as current or a counsel promise as compliance.',
  'multi_order_current_position', 'English', 'SUPPORTED',
  'Latest order expressly records errors uncorrected and renews LAC filing; earlier three-week period is superseded by four weeks, not completed.'),
 '4806-active-action': (
  ['wpc4806-2014-feb2020-p1', 'wpc4806-2014-apr2022-p1',
   'wpc4806-2014-p1', 'wpc4806-2014-p2', 'wpc4806-2014-p3', 'wpc4806-2014-p4'],
  ['wpc4806-2014-p2', 'wpc4806-2014-p3'],
  'तीन आदेशों के बाद LAC का कौन सा निर्देश अभी सक्रिय है? केवल नवीन निर्देश चुनें; लागत तभी लागू है जब चार सप्ताह में affidavit न दिया जाए।',
  'office_action_detection', 'Hindi', 'SUPPORTED',
  'Renewed filing and conditional costs are selected. Expired prior opportunities are not new simultaneous obligations.'),
 '4806-compliance': (
  ['wpc4806-2014-feb2020-p0', 'wpc4806-2014-apr2022-p0',
   'wpc4806-2014-p1', 'wpc4806-2014-p2'], [],
  'Affidavit ki galtiyan theek ho gayi thi ya bas baar-baar waqt mila? Kya Court ne completed correction record ki?',
  'compliance_state', 'RomanHindi', 'NOT_CONFIRMED_COMPLETE',
  'No selected completed-compliance proposition; latest observation explicitly says errors not corrected.'),
 '13932-position': (
  ['wpc13932-2025-sep2025-p0', 'wpc13932-2025-sep2025-p1', 'wpc13932-2025-sep2025-p2',
   'wpc13932-2025-feb03-p1', 'wpc13932-2025-p0', 'wpc13932-2025-p1',
   'wpc13932-2025-p2', 'wpc13932-2025-p5'],
  ['wpc13932-2025-sep2025-p0', 'wpc13932-2025-sep2025-p1', 'wpc13932-2025-sep2025-p2',
   'wpc13932-2025-p0', 'wpc13932-2025-p1', 'wpc13932-2025-p2', 'wpc13932-2025-p5'],
  'Is case ka current scene kya hai in teen supplied orders se? Purana Award historical hai, title/release petitioner claim hai, applications ka disposal writ ka disposal nahi. Latest meaningful developments aur Court date batao.',
  'multi_order_current_position', 'Hinglish', 'SUPPORTED',
  'Substantive original relief remains a claimed dispute; latest application disposal and original-owner impleadment do not dispose of the writ.'),
 '13932-current-obligation': (
  ['wpc13932-2025-sep2025-p3', 'wpc13932-2025-feb03-p1',
   'wpc13932-2025-p0', 'wpc13932-2025-p2', 'wpc13932-2025-p4', 'wpc13932-2025-p5'], [],
  'Do these three orders expressly assign an active obligation to LAC itself, rather than a generic respondent or petitioner?',
  'office_action_detection', 'English', 'NONE',
  'Do not infer a specific LAC assignment from generic respondents, petitioner memo duty or counsel names.'),
 '6108-position': (
  ['wpc6108-2015-nov2025-p0', 'wpc6108-2015-nov2025-p2',
   'wpc6108-2015-feb25-p0', 'wpc6108-2015-feb25-p1',
   'wpc6108-2015-feb25-p2', 'wpc6108-2015-p0', 'wpc6108-2015-p1', 'wpc6108-2015-p2'],
  ['wpc6108-2015-nov2025-p0', 'wpc6108-2015-feb25-p1',
   'wpc6108-2015-feb25-p2', 'wpc6108-2015-p0', 'wpc6108-2015-p1', 'wpc6108-2015-p2'],
  'What is the latest supplied position across November, February and March? Distinguish the earlier blank-template undertaking now performed from the separate populated-template obligation, and do not identify respondent 3 as LAC without evidence.',
  'multi_order_current_position', 'English', 'SUPPORTED',
  'Blank-template preparation is recorded performed, while populated LAC template filing is renewed and unconfirmed. Master-list completion is not established by silence.'),
 '6108-active-action': (
  ['wpc6108-2015-nov2025-p2', 'wpc6108-2015-feb25-p1',
   'wpc6108-2015-feb25-p3', 'wpc6108-2015-feb25-p4',
   'wpc6108-2015-p0', 'wpc6108-2015-p1'], ['wpc6108-2015-p1'],
  'Teen orders ke baad latest renewed LAC template action kya hai? Blank template ban jana, R3 affidavit file hona aur LAC filled template file karna alag hain.',
  'office_action_detection', 'RomanHindi', 'SUPPORTED',
  'Latest renewed populated-template filing direction only; do not count DDA/petitioner undertaking as LAC compliance.'),
 '6108-compliance': (
  ['wpc6108-2015-nov2025-p2', 'wpc6108-2015-feb25-p1',
   'wpc6108-2015-feb25-p2', 'wpc6108-2015-p0', 'wpc6108-2015-p1'],
  ['wpc6108-2015-feb25-p1'],
  'What earlier performance does the Court confirm across this supplied chain? Do not equate a blank template presented by petitioner/DDA counsel with completed populated LAC filing.',
  'compliance_state', 'English', 'SUPPORTED',
  'Confirmed earlier undertaking is specifically the blank template; actor distinction is preserved.'),
}

# Distinct chain decisions, not paraphrases: each focuses on a different
# lifecycle/dispute/schedule question and has a separately authored target.
# Twelve questions still represent FOUR independent three-order families.
CHAIN_DECISIONS.update({
 '4806-latest-development': (
  ['wpc4806-2014-feb2020-p0', 'wpc4806-2014-apr2022-p0',
   'wpc4806-2014-p1', 'wpc4806-2014-p2', 'wpc4806-2014-p3'],
  ['wpc4806-2014-p1', 'wpc4806-2014-p2', 'wpc4806-2014-p3'],
  'What changed in the latest supplied order after the earlier promise and repeated extension? Include the uncorrected position and renewed conditional consequence, not obsolete opportunities.',
  'multi_order_current_position', 'English', 'SUPPORTED',
  'Current non-correction plus renewed four-week opportunity and conditional costs; never presume completion.'),
 '4806-unresolved-dispute': (
  ['wpc4806-2014-feb2020-p0', 'wpc4806-2014-apr2022-p0', 'wpc4806-2014-p1'],
  ['wpc4806-2014-p1'],
  'In teen orders ke baad corrected affidavit ka unresolved factual position kya hai? Promise aur time grant se correction hui maan mat lena.',
  'multi_order_current_position', 'RomanHindi', 'SUPPORTED',
  'Latest Court expressly says errors still not corrected; earlier counsel promises do not override it.'),
 '13932-impleadment-lifecycle': (
  ['wpc13932-2025-sep2025-p0', 'wpc13932-2025-feb03-p0',
   'wpc13932-2025-feb03-p1', 'wpc13932-2025-p0', 'wpc13932-2025-p1'],
  ['wpc13932-2025-feb03-p1', 'wpc13932-2025-p1'],
  'Across these orders, distinguish the original-owner impleadment promise, completed service, and later Court impleadment. What has actually progressed?',
  'multi_order_current_position', 'English', 'SUPPORTED',
  'Service was recorded in February; operative impleadment is in August. Mere earlier promise is not completion.'),
 '13932-land-dispute-position': (
  ['wpc13932-2025-sep2025-p0', 'wpc13932-2025-sep2025-p1',
   'wpc13932-2025-sep2025-p2', 'wpc13932-2025-feb03-p1', 'wpc13932-2025-p0'],
  ['wpc13932-2025-sep2025-p0', 'wpc13932-2025-sep2025-p1',
   'wpc13932-2025-sep2025-p2', 'wpc13932-2025-p0'],
  'इन तीन आदेशों से क्या भूमि/title विवाद समाप्त हुआ? मूल मांग और historical Award अलग बताएं; बाद का application disposal writ disposal या ownership declaration नहीं है।',
  'multi_order_current_position', 'Hindi', 'SUPPORTED',
  'Original disputed relief remains a claim; historical acquisition Award and limited application disposal do not decide title.'),
 '6108-template-lifecycle': (
  ['wpc6108-2015-nov2025-p2', 'wpc6108-2015-feb25-p1',
   'wpc6108-2015-feb25-p2', 'wpc6108-2015-feb25-p3',
   'wpc6108-2015-p0', 'wpc6108-2015-p1'],
  ['wpc6108-2015-feb25-p1', 'wpc6108-2015-feb25-p2', 'wpc6108-2015-p1'],
  'Ab template work ki position kya hai: kaunsa stage Court ne completed record kiya aur kaunsa LAC stage abhi direction hai? R3 affidavit ko LAC completion na kaho.',
  'multi_order_current_position', 'Hinglish', 'SUPPORTED',
  'Prepared/finalized blank template is different from renewed populated-template filing; actor stays attributed.'),
 '6108-latest-listing': (
  ['wpc6108-2015-nov2025-p3', 'wpc6108-2015-feb25-p5', 'wpc6108-2015-p2'],
  ['wpc6108-2015-p2'],
  'Teen actual orders ki chronology ke baad latest supplied Court listing kya hai? November order ki purani February listing ab current nahi hai.',
  'multi_order_current_position', 'RomanHindi', 'SUPPORTED',
  'March confirms the existing May Court listing; no order on the listed May date is fabricated.'),
 '9093-disputed-possession-position': (
  ['wpc9093-2022-apr2024-p0', 'wpc9093-2022-apr2024-p1',
   'wpc9093-2022-feb09-p0', 'wpc9093-2022-p1', 'wpc9093-2022-p4'],
  ['wpc9093-2022-apr2024-p0', 'wpc9093-2022-apr2024-p1',
   'wpc9093-2022-feb09-p0', 'wpc9093-2022-p1'],
  'What possession/compensation dispute remains in the supplied chain? Separate petitioner demand, SDM report and the Court’s latest view of conflicting authorities; do not establish a handover or payment.',
  'multi_order_current_position', 'English', 'SUPPORTED',
  'The report location and petitioner assertions stay attributed; Court records disagreement rather than settling possession.'),
 '9093-meeting-vs-hearing': (
  ['wpc9093-2022-apr2024-p3', 'wpc9093-2022-feb09-p4',
   'wpc9093-2022-p2', 'wpc9093-2022-p3', 'wpc9093-2022-p6'],
  ['wpc9093-2022-p3', 'wpc9093-2022-p6'],
  'इन तीन आदेशों के बाद कौन सी latest तारीख office meeting की है और कौन सी Court hearing की? पहले आदेशों की listings को current dates न कहें।',
  'multi_order_current_position', 'Hindi', 'SUPPORTED',
  'September interdepartmental meeting and December Court listing are separate; prior Court listings are superseded.'),
})

for key, (supplied, selected, question, task, language, outcome, _) in CHAIN_DECISIONS.items():
    EXAMPLES.append((key, task, supplied, selected, question, language, outcome))

# Explicit approval is source/task review, not a model-generated label. Export
# still requires source binding, runtime compatibility and minimization.
VERIFIED_IDS = frozenset(a[0] for a in EXAMPLES)
