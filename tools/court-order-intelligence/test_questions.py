import unittest
from questions import answer, retrieve, INSUFFICIENT
from test_worker import fact, order

class SelectAll:
    version='test'
    def extract(self,instructions,source,schema,feedback=''):
        import json
        facts=json.loads(source)['availableEvidence']
        return {'claims':[{'factId':f['factId']} for f in facts[:4]]}

def artifact():
    return {'caseId':'case-a','caseNumber':'W.P.(C) 1/2026','orders':[
        order([fact('The LAC submits compensation was deposited.',field='compensation',category='LAC_OR_RESPONDENT_SUBMISSION',deadlineText=None)],orderDate='2025-01-01'),
        order([fact('The petitioner claims compensation is unpaid.',field='compensation',category='PETITIONER_SUBMISSION',actor=None,deadlineText=None)],orderDate='2025-05-01'),
        order([fact()],orderDate='2026-01-01')], 'beforeNextHearing':[]}

class QuestionTests(unittest.TestCase):
    def test_verified_direction_remains_grounded_when_other_passage_uncertain(self):
        data=artifact()
        data['orders'][-1].update(status='NeedsReview',coverage={'allSelectedChunksProcessed':True})
        data['orders'][-1]['facts'].append(fact('Uncertain claim.',category='PETITIONER_SUBMISSION',scope='Uncertain'))
        result=answer(data,'case-a','What did the latest order direct?',SelectAll())
        self.assertEqual(1,len(result['claims']))
        self.assertEqual('Court direction',result['claims'][0]['attribution'])

    def test_latest_direction_grounded(self):
        result=answer(artifact(),'case-a','What did the latest order direct?',SelectAll())
        self.assertEqual('2026-01-01',result['claims'][0]['source']['orderDate'])
        self.assertEqual(1,result['claims'][0]['source']['page'])
        self.assertEqual('Court direction',result['claims'][0]['attribution'])

    def test_direction_retrieval_includes_operative_filing_and_listing_fields(self):
        data=artifact()
        data['orders'][-1]['facts']=[fact(field='filing'),fact('Renotify on 01.03.2026.',field='nextHearing',actor=None,deadlineText=None)]
        self.assertEqual(2,len(answer(data,'case-a','What did the latest order direct?',SelectAll())['claims']))

    def test_possession_direction_is_not_lost_under_direction_field(self):
        data=artifact()
        text='The parties shall maintain status quo regarding possession.'
        data['orders'][-1]['facts']=[fact(text,actor=None,deadlineText=None)]
        result=answer(data,'case-a','What is the possession position?',SelectAll())
        self.assertEqual(text,result['claims'][0]['text'])
        self.assertEqual('Court direction',result['claims'][0]['attribution'])

    def test_quoted_precedent_cannot_answer_current_case_compensation(self):
        data=artifact()
        data['orders']=[order([fact('Compensation was paid in W.P.(C) 99/2019.',category='COURT_FINDING',field='compensation',scope='Quoted',actor=None,deadlineText=None)])]
        self.assertEqual(INSUFFICIENT,answer(data,'case-a','What compensation was paid?',SelectAll())['answer'])
        data['orders'][0]['facts'][0]['scope']='Historical'
        self.assertEqual(INSUFFICIENT,answer(data,'case-a','What compensation was paid?',SelectAll())['answer'])

    def test_compensation_across_orders_attributed(self):
        result=answer(artifact(),'case-a','What has happened regarding compensation?',SelectAll())
        self.assertEqual(2,len(result['claims']))
        self.assertIn('submission (not an established Court fact)',result['claims'][0]['attribution'])
        self.assertIn('Petitioner submission',result['claims'][1]['attribution'])

    def test_timeline_year_retrieval(self):
        entries=retrieve(artifact(),'What happened in this case during 2025?')
        self.assertEqual(2,len(entries))
        self.assertTrue(all(x['source']['orderDate'].startswith('2025') for x in entries))

    def test_timeline_year_range(self):
        entries=retrieve(artifact(),'What happened between 2024 and 2026?')
        self.assertEqual(3,len(entries))

    def test_unsupported_fact_insufficient(self):
        result=answer(artifact(),'case-a','What is the judge home address?',SelectAll())
        self.assertEqual(INSUFFICIENT,result['answer'])

    def test_wrong_case_artifact_rejected(self):
        with self.assertRaises(ValueError):answer(artifact(),'case-b','What happened?',SelectAll())

    def test_question_about_other_matter_does_not_use_current_facts(self):
        for question in ['What compensation was paid in W.P.(C) 999/2025?', 'Compare compensation across cases']:
            self.assertEqual(INSUFFICIENT,answer(artifact(),'case-a',question,SelectAll())['answer'])

    def test_citation_is_derived_not_invented_by_model(self):
        class WrongCitation:
            def extract(self,*args):return {'claims':[{'factId':99,'text':'invented'}]}
        result=answer(artifact(),'case-a','What did latest order direct?',WrongCitation())
        self.assertTrue(result['claims'])
        self.assertNotIn('invented',result['answer'])
        self.assertEqual('2026-01-01',result['claims'][0]['source']['orderDate'])

    def test_removed_negation_rejected(self):
        class FalseFact:
            def extract(self,*args):return {'claims':[{'factId':1,'text':'compensation is paid'}]}
        result=answer(artifact(),'case-a','What has happened regarding compensation?',FalseFact())
        self.assertTrue(any('unpaid' in claim['text'] for claim in result['claims']))
        self.assertNotIn('compensation is paid',result['answer'])

    def test_ai_service_failure_propagates_to_calm_runtime_boundary(self):
        class Offline:
            def extract(self,*args):raise ConnectionError('local offline')
        with self.assertRaises(ConnectionError):answer(artifact(),'case-a','What did latest order direct?',Offline())

    def test_does_not_retrieve_another_case(self):
        import inspect, questions
        self.assertNotIn('glob(',inspect.getsource(questions))
        self.assertNotIn('requests.',inspect.getsource(questions))

if __name__=='__main__':unittest.main()
