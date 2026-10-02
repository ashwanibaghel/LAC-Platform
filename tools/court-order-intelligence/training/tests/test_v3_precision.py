import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'training'))
from semantic_gate import validate_claims
from v3_semantics import party_speech, validate_fact
from v3_contract import parse_v3_output
from schema.contracts import ANCHOR_SCHEMA
from test_v3_semantic_gate import entry


class V3PrecisionTests(unittest.TestCase):
    def claims(self, fact, task):
        return validate_claims({'claims': [{'factId': 0}]}, [fact], task)

    def test_modal_verbs_are_not_speech_but_reported_modal_still_is(self):
        for text in ('LAC shall submit the report.', 'Counsel shall seek instructions.',
                     'Let the affidavit be submitted by LAC.',
                     'The Petitioner is granted an opportunity to argue the matter.',
                     'The prayer for interim relief is not made out at this stage.'):
            self.assertFalse(party_speech(text), text)
        for text in ('Counsel submits that LAC shall submit a report.',
                     'Counsel submitted that the affidavit should be submitted.',
                     'The petitioner seeks permission to argue the matter.',
                     'The petitioner argues that compensation was paid.',
                     'According to counsel, the counter affidavit establishes payment.',
                     'The counter affidavit says compensation has been paid.',
                     'The counter affidavit compensation has been paid.'):
            self.assertTrue(party_speech(text), text)

    def test_filing_duties_keep_original_field_and_every_condition(self):
        for text in ('LAC shall submit a report, preferably within four weeks.',
                     'Last opportunity is granted to respondent/LAC to file an affidavit within three weeks.',
                     'The filing by LAC be completed by 30.04.2026, if required.'):
            fact = entry(text, field='filing')
            before = copy.deepcopy(fact)
            self.claims(fact, 'office_action_detection')
            self.assertEqual(fact, before)
            self.assertEqual(fact['field'], 'filing')
        for text in ('Counsel for LAC submits that the affidavit will be filed.',
                     'LAC has filed the affidavit.', 'The respondent will file an affidavit.'):
            with self.assertRaises(ValueError):
                self.claims(entry(text, field='filing'), 'office_action_detection')

    def test_performance_must_be_selected_linked_actor_supported_and_unnegated(self):
        for text in ('Pursuant to the direction, GNCTD has now filed the affidavit.',
                     'In compliance, Petitioner/DDA counsel have placed before the Court the blank template.'):
            fact = entry(text, 'RECORDED_COMPLIANCE', 'compliance')
            fact['actor'] = 'GNCTD' if 'GNCTD' in text else 'Petitioner/DDA'
            self.claims(fact, 'compliance_state')
        for text in ('Counsel submits that LAC has now filed in compliance.',
                     'In compliance, LAC has not filed the affidavit.',
                     'In compliance, LAC shall submit the report.',
                     'LAC has filed a different document.',
                     'In compliance, DDA has now filed the affidavit.'):
            with self.assertRaises(ValueError):
                self.claims(entry(text, 'RECORDED_COMPLIANCE', 'compliance'), 'compliance_state')
        fact = entry('In compliance, LAC shall file the affidavit.', 'RECORDED_COMPLIANCE', 'compliance')
        fact['source']['evidence'] += ' LAC has filed some other affidavit.'
        with self.assertRaises(ValueError):
            self.claims(fact, 'compliance_state')

    def test_actor_substrings_and_cross_source_context_cannot_authorize_action(self):
        fact = entry('LACK shall file an affidavit.', field='filing')
        with self.assertRaises(ValueError):
            self.claims(fact, 'office_action_detection')
        fact = entry('Let the affidavit be filed.', field='filing')
        fact['source']['sha256'] = 'a' * 64
        fact['sourceContext'] = [dict(page=2, evidence='LAC is the named respondent.',
            sha256='b' * 64, officialUrl=fact['source']['officialUrl'], orderDate='2025-01-02')]
        with self.assertRaises(ValueError):
            self.claims(fact, 'office_action_detection')

    def anchor_record(self, prefix):
        text = 'The named authority shall file the report.'
        return dict(contract='anchors', task='attribution_classification', input=dict(
            documentOrderDate='2025-01-02', sourceSha256='a' * 64,
            anchors=[dict(anchorId=0, page=2, text=text, actors=[], quoted=False, speechRole=None)],
            sourceWindows={'0': dict(page=2, orderDate='2025-01-02', sha256='a' * 64,
                                     evidence=prefix + ' ' + text)}))

    def test_real_native_judicial_reset_not_selected_anchor_adjacency(self):
        payload = json.dumps({'facts': [dict(anchorId=0, category='COURT_DIRECTION', field='filing', scope='Current')], 'needsReview': False})
        record = self.anchor_record('Counsel submits a disputed assertion. Considering the submissions, the matter is referred to the named authority.')
        before = copy.deepcopy(record)
        parse_v3_output(payload, record, {'anchors': ANCHOR_SCHEMA}, ROOT)
        self.assertEqual(record, before)
        bad = self.anchor_record('Counsel submits that the following requirement is necessary:')
        with self.assertRaises(ValueError):
            parse_v3_output(payload, bad, {'anchors': ANCHOR_SCHEMA}, ROOT)
        from v3_semantics import judicial_context_safe
        selected = '10. The meeting shall take place on 14th September, 2026.'
        self.assertTrue(judicial_context_safe(selected, 'The land is stated to belong to petitioner. ' + selected))
        self.assertFalse(judicial_context_safe(selected, 'Counsel submits the following: ' + selected))
        for change in ({'page': 3}, {'sha256': 'b' * 64}, {'orderDate': '2025-01-03'}):
            altered = copy.deepcopy(record)
            altered['input']['sourceWindows']['0'].update(change)
            with self.assertRaises(ValueError):
                parse_v3_output(payload, altered, {'anchors': ANCHOR_SCHEMA}, ROOT)
        quoted = copy.deepcopy(record)
        quoted['input']['anchors'][0]['quoted'] = True
        with self.assertRaises(ValueError):
            parse_v3_output(payload, quoted, {'anchors': ANCHOR_SCHEMA}, ROOT)

    def test_production_and_frozen_parser_unchanged_and_no_gold_guard_lookup(self):
        for file in (ROOT / 'v3_semantics.py', ROOT / 'semantic_gate.py', ROOT / 'training/v3_contract.py'):
            text = file.read_text(encoding='utf-8')
            for forbidden in ('W.P.(C)', 'expected[', "record['target']", 'pilot_review', 'v2_tasks'):
                self.assertNotIn(forbidden, text)
        from semantics import party_speech as legacy_speech
        self.assertTrue(legacy_speech('LAC shall submit the report.'))
        self.assertFalse(party_speech('LAC shall submit the report.'))

    def test_all_four_models_share_v3_gate_independent_of_gold(self):
        sys.path.insert(0, str(ROOT / 'training/kaggle'))
        from v3_evaluation import assess_output, MODES
        from schema.contracts import ANSWER_SCHEMA
        fact = entry('LAC shall submit a report.', field='filing')
        record = dict(id='synthetic-test-only', matter_id='synthetic', task='office_action_detection', contract='claims',
                      input={'availableEvidence': [fact]}, expected={'claims': [{'factId': 0}]}, outcome='SUPPORTED', language='English')
        result = [assess_output(mode, '{"claims":[{"factId":0}]}', record,
                 {'claims': ANSWER_SCHEMA}, ROOT) for mode in MODES]
        self.assertTrue(all(r['parser_accepted'] for r in result))
        record['expected'] = {'claims': []}
        self.assertTrue(all(assess_output(mode, '{"claims":[{"factId":0}]}', record,
                 {'claims': ANSWER_SCHEMA}, ROOT)['parser_accepted'] for mode in MODES))
        fact['directionLifecycle'] = 'UNKNOWN'
        self.assertFalse(any(assess_output(mode, '{"claims":[{"factId":0}]}', record,
                  {'claims': ANSWER_SCHEMA}, ROOT)['parser_accepted'] for mode in MODES))

    def test_missing_actor_future_performance_and_party_contention_stay_rejected(self):
        self.assertTrue(party_speech('The contention of the petitioner is that payment has been made.'))
        for state in ('UNKNOWN', 'COMPLETED', 'SUPERSEDED'):
            fact = entry('LAC shall submit a report.', field='filing')
            fact['directionLifecycle'] = state
            with self.assertRaises(ValueError):
                self.claims(fact, 'office_action_detection')

    def test_context_compaction_is_lossless_and_target_independent(self):
        from v3_context import compact_input, restore_input
        from semantic_gate import chronology_context
        fact = entry('LAC shall submit a report.', field='filing')
        fact['source']['sha256'] = 'a' * 64
        fact['sourceContext'] = [dict(fact['source'], evidence='Counsel submitted a disputed claim. ' + fact['text'])]
        value = dict(availableEvidence=[fact], chronologyState=chronology_context([fact]))
        before = copy.deepcopy(value)
        compacted = compact_input(value)
        self.assertEqual(value, before)
        self.assertEqual(restore_input(compacted), value)
        self.assertIn('Counsel submitted a disputed claim.', compacted['availableEvidence'][0]['sourceContext'][0]['evidence'])
        self.assertLess(len(json.dumps(compacted)), len(json.dumps(value)))
        record = dict(contract='claims', task='office_action_detection', input=compacted)
        from schema.contracts import ANSWER_SCHEMA
        parse_v3_output('{"claims":[{"factId":0}]}', record, {'claims': ANSWER_SCHEMA}, ROOT)
        compacted['availableEvidence'][0]['source']['sourceRef'] = 9
        with self.assertRaises(ValueError):
            parse_v3_output('{"claims":[{"factId":0}]}', record, {'claims': ANSWER_SCHEMA}, ROOT)


if __name__ == '__main__':
    unittest.main()
