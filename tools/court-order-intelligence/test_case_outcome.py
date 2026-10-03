import copy
import unittest
from query_intents import normalize
from questions import retrieve, answer
from test_worker import fact, order
from test_questions import SelectAll

class CaseOutcomeTests(unittest.TestCase):
    def data(self):
        return {'caseId':'a','caseNumber':'W.P.(C) 1/2026','canonicalStatus':'Pending',
            'beforeNextHearing':[], 'orders':[
                order([fact('The petition was dismissed.',category='DISPOSITION',field='disposition',scope='Quoted')],orderDate='2025-01-01'),
                order([fact('The petition is disposed of in these terms.',category='DISPOSITION',field='disposition',actor=None,deadlineText=None),
                    fact('The petitioner is permitted to file a claim.',category='PROCEDURAL_EVENT',field='filing',actor='Petitioner',deadlineText=None),
                    fact('After examining the documents, the LAC may release the compensation.',category='COMPENSATION_FACT',field='compensation',deadlineText=None),
                    fact('If the need arises, the LAC to make a reference and the same be adjudicated within six months.',category='PROCEDURAL_EVENT',field='referenceToAdj',deadlineText=None),
                    fact('The petitioner says the case is pending.',category='PETITIONER_SUBMISSION',field='context',actor=None,deadlineText=None)])]}

    def test_outcome_variants_and_current_only(self):
        for q in ['Case dispose hua hai ya pending hai?','Petition disposed hai ya pending?',
                  'Court ne case dispose kiya?','Final outcome kya hai?',
                  'What is the final outcome?','Court ka final order kya tha?',
                  'Was the petition disposed or is it pending?','What was the final disposition?',
                  'मामला निस्तारित हुआ या लंबित है?']:
            with self.subTest(q=q):
                self.assertIn('case_outcome',normalize(q)['topics'])
                self.assertNotIn('lac_action',normalize(q)['topics'])
                entries=retrieve(self.data(),q)
                self.assertEqual(entries[0]['category'],'DISPOSITION')
                self.assertTrue(all(e['scope']=='Current' and e['source']['orderDate']=='2026-01-01' for e in entries))
                self.assertFalse(any(e['category']=='PETITIONER_SUBMISSION' for e in entries))

    def test_combined_outcome_keeps_conditions_without_db_mutation(self):
        data=self.data(); before=copy.deepcopy(data)
        result=answer(data,'a','Case dispose hua hai ya pending hai aur Court ne exactly kya order diya?',SelectAll())
        text=result['answer']
        self.assertIn('disposed of',text)
        self.assertIn('permitted to file',text)
        self.assertIn('may release',text)
        self.assertIn('If the need arises',text)
        self.assertIn('not a mandatory action',text)
        self.assertEqual(data,before)

    def test_office_pending_is_not_case_outcome(self):
        for q in ['LAC ka kya pending hai?','Kaunsa compliance pending hai?','before next hearing kya pending hai?']:
            self.assertIn('lac_action',normalize(q)['topics'])
            self.assertNotIn('case_outcome',normalize(q)['topics'])
