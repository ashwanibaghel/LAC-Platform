import copy
import unittest
from questions import retrieve, compose, answer
from test_questions import SelectAll
from test_worker import fact, order

class ActionRetrievalTests(unittest.TestCase):
    def data(self, mandatory=False, conditional=False):
        facts=[]
        if mandatory: facts.append(fact())
        if conditional:
            facts.extend([fact('After examining the documents, the LAC may release the compensation.',category='COMPENSATION_FACT',field='compensation',deadlineText=None),
                fact('Thereafter, if the need arises, the LAC to make a reference under Section 30 and 31 and the same be adjudicated within six months.',category='PROCEDURAL_EVENT',field='referenceToAdj',deadlineText='within six months'),
                fact('The LAC shall pay within eight weeks.',scope='Quoted')])
        return {'caseId':'a','caseNumber':'W.P.(C) 1/2026','orders':[order(facts)],
                'beforeNextHearing':[{'text':facts[0]['value']}] if mandatory else []}

    def test_action_union_does_not_mutate_safety_collection(self):
        for mandatory,conditional in [(True,False),(False,True),(True,True)]:
            data=self.data(mandatory,conditional); before=copy.deepcopy(data)
            for q in ['LAC ko ab kya karna hai?','LAC ko kya action lena hai?',
                      'What does LAC need to do?','LAC ke liye Court ki kya direction hai?']:
                entries=retrieve(data,q)
                self.assertEqual({e['actionClass'] for e in entries},
                    ({'Mandatory'} if mandatory else set())|({'Conditional'} if conditional else set()))
                self.assertTrue(all(e['scope']=='Current' for e in entries))
                self.assertFalse(compose(entries)['insufficientEvidence'])
            self.assertEqual(data,before)

    def test_reference_condition_and_adjudication_period_exact(self):
        result=answer(self.data(conditional=True),'a','Section 30/31 reference kab banana hai?',SelectAll())
        self.assertFalse(result['insufficientEvidence'])
        claim=result['claims'][0]
        self.assertIn('if the need arises',claim['text'])
        self.assertIn('same be adjudicated within six months',claim['text'])
        self.assertIn('not a mandatory action',claim['attribution'])

    def test_empty_action_evidence_stays_insufficient(self):
        self.assertTrue(compose(retrieve(self.data(),'LAC ko ab kya karna hai?'))['insufficientEvidence'])
