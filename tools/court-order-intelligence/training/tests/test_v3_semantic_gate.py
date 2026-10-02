import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'training'))
from semantic_gate import validate_claims
from v3_contract import parse_v3_output, context_for_v3
from questions import ANSWER_SCHEMA


def entry(text, role='COURT_DIRECTION', field='direction', **extra):
    return dict(factId=0, text=text, category=role, scope='Current', field=field,
                actor='LAC', directionLifecycle='OPEN',
                source={'orderDate': '2025-01-02', 'page': 2, 'evidence': text,
                        'officialUrl': 'https://delhihighcourt.nic.in/example.pdf'}, **extra)


class V3SemanticGateTests(unittest.TestCase):
    def check(self, fact, task, **kwargs):
        return validate_claims({'claims': [{'factId': 0}]}, [fact], task, **kwargs)

    def test_direction_time_promise_expiry_silence_not_completion(self):
        for text in ('LAC is directed to forward the reference within one month.',
                     'Time is granted to LAC to file the affidavit.',
                     'Counsel for LAC submits that the affidavit will be filed.',
                     'The period has expired; no compliance has been recorded.'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                self.check(entry(text), 'compliance_state')

    def test_recorded_performance_permitted_not_party_allegation_or_negation(self):
        fact = entry('In compliance with the direction, the LAC reference has been forwarded.',
                     'RECORDED_COMPLIANCE', 'compliance')
        self.check(fact, 'compliance_state')
        for text in ('LAC submits that the reference has been forwarded in compliance.',
                     'The reference has not been forwarded in compliance.'):
            with self.assertRaises(ValueError):
                self.check(entry(text, 'RECORDED_COMPLIANCE', 'compliance'), 'compliance_state')

    def test_action_preserves_adjacent_actor_qualified_and_conditional_text(self):
        fact = entry('The respondent is LAC. Let the affidavit be filed, preferably within four weeks, if required.')
        self.check(fact, 'office_action_detection')
        self.assertIn('preferably', fact['text'])
        self.assertIn('if required', fact['text'])

    def test_unknown_actor_nonoperative_historical_completed_actions_rejected(self):
        good = entry('LAC is directed to file the affidavit.')
        for change in ({'actor': None}, {'actor': 'DDA'}, {'scope': 'Quoted'},
                       {'directionLifecycle': 'COMPLETED'}, {'directionLifecycle': 'SUPERSEDED'},
                       {'directionLifecycle': 'UNKNOWN'}, {'category': 'COURT_OBSERVATION'},
                       {'text': 'LAC has requested time.',
                        'source': dict(good['source'], evidence='LAC has requested time.')}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.check(dict(good, **change), 'office_action_detection')

    def test_next_hearing_requires_listing_not_historical_or_factual_date(self):
        good = entry('List on 5 March 2025.', field='nextHearing', dateLifecycle='LATEST_CONFIRMED')
        self.check(good, 'date_specific_retrieval_or_QA', intent='next_hearing')
        for change in ({'dateLifecycle': 'UNKNOWN'}, {'scope': 'Historical'},
                       {'field': 'possession'}, {'category': 'PETITIONER_SUBMISSION'}):
            with self.assertRaises(ValueError):
                self.check(dict(good, **change), 'date_specific_retrieval_or_QA', intent='next_hearing')

    def test_stale_direction_not_selected_as_current_position(self):
        good = entry('LAC is directed to file the affidavit.')
        self.check(good, 'multi_order_current_position')
        with self.assertRaises(ValueError):
            self.check(dict(good, directionLifecycle='SUPERSEDED'), 'multi_order_current_position')

    def test_structural_gate_first_no_repair_gold_or_expected_access(self):
        record = {'contract': 'claims', 'task': 'compliance_state',
                  'input': {'availableEvidence': [entry('LAC is directed to file an affidavit.')]},
                  'expected': {'claims': [{'factId': 0}]}}
        before = copy.deepcopy(record)
        for text in ('prose {"claims":[]}', '{"claims":[{"factId":99}]}',
                     '{"claims":[{"factId":0}]}'):
            with self.assertRaises(ValueError):
                parse_v3_output(text, record, {'claims': ANSWER_SCHEMA}, ROOT)
        self.assertEqual(before, record)
        self.assertEqual(parse_v3_output('{"claims":[]}', record, {'claims': ANSWER_SCHEMA}, ROOT), {'claims': []})

    def test_context_chronology_actor_unknowns_and_verbatim_preserved(self):
        first = entry('LAC is directed to file an affidavit.')
        second = dict(entry('List on 5 March 2025.', field='nextHearing'), factId=1,
                      source=dict(first['source'], orderDate='2025-02-02', evidence='List on 5 March 2025.'))
        second.pop('directionLifecycle')
        value = {'question': 'Current position?', 'availableEvidence': [first, second]}
        before = copy.deepcopy(value)
        result = context_for_v3(value)
        self.assertEqual(before, value)
        self.assertEqual(result['availableEvidence'], value['availableEvidence'])
        state = result['chronologyState']
        self.assertEqual(state[0]['actor'], 'LAC')
        self.assertFalse(state[0]['latestSuppliedOrder'])
        self.assertTrue(state[1]['latestSuppliedOrder'])
        self.assertEqual(state[1]['directionLifecycle'], 'UNKNOWN')
        self.assertEqual(state[1]['latestMeaningfulDevelopment'], 'UNKNOWN')
        self.assertNotIn('expected', json.dumps(result))


if __name__ == '__main__':
    unittest.main()
