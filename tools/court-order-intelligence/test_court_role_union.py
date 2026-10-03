import unittest
from questions import retrieve, compose
from test_worker import fact, order

class CourtRoleUnionTests(unittest.TestCase):
    def data(self):
        return {'caseId':'a','caseNumber':'W.P.(C) 1/2026','orders':[order([
            fact('The Court finds the refusal unlawful.',category='COURT_FINDING',field='finding',actor=None,deadlineText=None),
            fact('The Court observed the record.',category='COURT_OBSERVATION',field='observation',actor=None,deadlineText=None),
            fact('If necessary, the LAC shall examine the application.',category='COURT_DIRECTION',field='direction',deadlineText=None),
            fact('The petition is disposed of.',category='DISPOSITION',field='disposition',actor=None,deadlineText=None),
            fact('The petitioner submits the refusal was unlawful.',category='PETITIONER_SUBMISSION',field='context',actor=None,deadlineText=None),
            fact('The LAC shall pay within eight weeks.',scope='Quoted')])], 'beforeNextHearing':[]}

    def test_combined_roles_union(self):
        for question in ['Court ne kya find/direct kiya?','What did the Court find and direct?']:
            roles={e['category'] for e in retrieve(self.data(),question)}
            self.assertEqual(roles,{'COURT_FINDING','COURT_DIRECTION','DISPOSITION'})
        self.assertEqual({e['category'] for e in retrieve(self.data(),'Court ne kya observe aur direct kiya?')},
                         {'COURT_OBSERVATION','COURT_DIRECTION'})

    def test_single_roles_and_outcome(self):
        self.assertEqual({e['category'] for e in retrieve(self.data(),'Court ne kya find kiya?')},{'COURT_FINDING'})
        entries=retrieve(self.data(),'Court ne kya direction di?')
        self.assertEqual({e['category'] for e in entries},{'COURT_DIRECTION'})
        self.assertIn('If necessary',entries[0]['text'])
        self.assertTrue(all(e['scope']=='Current' for e in entries))
        outcome=compose(retrieve(self.data(),'Court ka final order kya tha?'))
        self.assertTrue(any(c['attribution']=='The Court decided' for c in outcome['claims']))
        self.assertFalse(any('submission' in c['attribution'].lower() for c in outcome['claims']))
