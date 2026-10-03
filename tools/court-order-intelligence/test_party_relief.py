import unittest
from query_intents import normalize
from questions import retrieve, compose

class PartyReliefTests(unittest.TestCase):
    def artifact(self, facts=True):
        text='The petitioner seeks relief against the refusal of his application.'
        return {'caseId':'a','caseNumber':'W.P.(C) 1/2025','orders':[{
            'status':'Validated','orderDate':'2025-01-01','officialUrl':'https://example.test/order.pdf',
            'facts':[{'category':'PETITIONER_SUBMISSION','field':'context','scope':'Current',
                      'value':text,'evidence':text,'page':1,'actor':None,'deadlineText':None,
                      'targetOrderDate':None,'targetActionText':None}] if facts else []}]}

    def test_named_party_relief_variants(self):
        for question in ['Petitioner kya maang raha hai?','Petitioner kya mang raha hai?',
                         'Petitioner kya chahta hai?','Petitioner ne kya relief maangi hai?',
                         'Petitioner ki prayer kya hai?','What is the petitioner seeking?',
                         'What relief does the petitioner seek?','What did the petitioner pray for?',
                         'याचिकाकर्ता क्या मांग रहा है?','याचिकाकर्ता ने क्या राहत मांगी है?']:
            with self.subTest(question=question):
                intent=normalize(question)
                self.assertEqual(intent['party'],'Petitioner')
                self.assertEqual(intent['topics'],['party_position'])
                entries=retrieve(self.artifact(),question)
                self.assertTrue(entries)
                self.assertEqual(entries[0]['category'],'PETITIONER_SUBMISSION')
                answer=compose(entries)
                self.assertIn('not an established Court fact',answer['claims'][0]['attribution'])
                self.assertEqual(answer['claims'][0]['source']['page'],1)

    def test_absent_submission_stays_insufficient(self):
        self.assertTrue(compose(retrieve(self.artifact(False),'Petitioner kya maang raha hai?'))['insufficientEvidence'])

    def test_generic_want_without_party_not_submission_intent(self):
        self.assertNotIn('party_position',normalize('What relief did Court grant?')['topics'])
        self.assertNotIn('party_position',normalize('What do we want?')['topics'])

if __name__=='__main__': unittest.main()
