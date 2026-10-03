import unittest
from anchors import expand, requires_present_scope
from query_intents import normalize
from questions import retrieve, compose


class FinalCorrectnessTests(unittest.TestCase):
    def anchor(self,text,quoted=False):
        return dict(anchorId=0,page=1,text=text,quoted=quoted,actors=[])

    def test_present_historical_rejected_not_rewritten(self):
        text='As of today, the required documents have not been filed.'
        a=self.anchor(text)
        with self.assertRaisesRegex(ValueError,'present state'):
            expand(dict(facts=[dict(anchorId=0,category='DOCUMENT_OR_FILING_FACT',field='filing',scope='Historical')],needsReview=False),[a],{1:text})
        with self.assertRaisesRegex(ValueError,'present state'):
            expand(dict(facts=[dict(anchorId=0,category='DOCUMENT_OR_FILING_FACT',field='filing',scope='Uncertain')],needsReview=False),[a],{1:text})

    def test_old_quoted_present_not_promoted(self):
        self.assertFalse(requires_present_scope(self.anchor('As of today, no payment has been made.',True)))

    def test_earlier_narration_remains_historical(self):
        self.assertFalse(requires_present_scope(self.anchor('In 2018 the position at present was disputed.')))
        self.assertFalse(requires_present_scope(self.anchor('The earlier order directed the LAC to file a reply.',True)))

    def test_generic_present_variants(self):
        for phrase in ('as of today','as on date','currently','at present'):
            self.assertTrue(requires_present_scope(self.anchor(phrase+', the record remains incomplete.')))

    def test_hinglish_full_story_retrieves_verified_evidence(self):
        text='The present petition is disposed of.'
        fact=dict(category='DISPOSITION',field='disposition',scope='Current',value=text,evidence=text,page=1,actor=None,deadlineText=None,targetOrderDate=None,targetActionText=None)
        artifact=dict(caseId='local',caseNumber='W.P.(C) 123/2025',orders=[dict(status='Validated',orderDate='2026-01-02',officialUrl='https://example.test/order.pdf',facts=[fact])])
        for question in ('Is case ka poora scene batao.','pura scene','case ka poora scene','matter ka poora scene','poora case batao','pura matter batao','overall case','complete case summary','पूरा मामला','पूरा केस'):
            intent=normalize(question)
            self.assertEqual(['general_case'],intent['topics'],question)
            self.assertTrue(intent['fullStory'])
            result=compose(retrieve(artifact,question,intent))
            self.assertFalse(result['insufficientEvidence'])
            self.assertEqual(text,result['claims'][0]['source']['evidence'])
            self.assertEqual(1,result['claims'][0]['source']['page'])

    def test_full_story_keeps_state_principal_disposal_and_conditions(self):
        pairs=[('PETITIONER_SUBMISSION','context','The petitioner seeks compensation.'),
               ('LAC_OR_RESPONDENT_SUBMISSION','context','The LAC contends no compensation was paid.'),
               ('COMPENSATION_FACT','compensation','As of today no compensation has been paid.'),
               ('COMPENSATION_FACT','compensation','After examining documents the LAC may release compensation.'),
               ('PROCEDURAL_EVENT','referenceToAdj','If required the LAC to make a reference.'),
               ('PROCEDURAL_EVENT','filing','The petitioner is permitted to file a claim.'),
               ('DISPOSITION','disposition','The present petition is disposed of.'),
               ('DISPOSITION','disposition','Pending applications are also disposed of.')]
        facts=[dict(category=role,field=field,scope='Current',value=text,evidence=text,page=1,actor=None,deadlineText=None,targetOrderDate=None,targetActionText=None) for role,field,text in pairs]
        artifact=dict(caseNumber='W.P.(C) 123/2025',orders=[dict(status='Validated',orderDate='2026-01-02',officialUrl='https://example.test/order.pdf',facts=facts)])
        result=retrieve(artifact,'Is case ka poora scene batao.')
        texts=[entry['text'] for entry in result]
        for index in (0,1,2,3,4,5,6): self.assertIn(pairs[index][2],texts)
        self.assertNotIn(pairs[7][2],texts)


if __name__=='__main__': unittest.main()
