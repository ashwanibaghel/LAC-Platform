import copy,json,unittest
from anchors import anchors_for,expand
from completeness import recover
from semantics import validate
from test_worker import fact


class ScopeDomainRecoveryTests(unittest.TestCase):
    text='Therefore, as of today, no compensation has been paid to any person.'
    def test_bad_scope_rejected_current_accepted(self):
        pages={1:self.text};a=anchors_for(pages)
        for scope in ('Historical','Uncertain'):
            payload=dict(facts=[dict(anchorId=0,category='COMPENSATION_FACT',field='compensation',scope=scope)],needsReview=False)
            before=copy.deepcopy(payload)
            with self.assertRaises(ValueError): expand(payload,a,pages)
            self.assertEqual(before,payload)
        result=expand(dict(facts=[dict(anchorId=0,category='COMPENSATION_FACT',field='compensation',scope='Current')],needsReview=False),a,pages)
        self.assertEqual('Current',result['facts'][0]['scope'])
    def test_quoted_and_earlier_state_remain_historical(self):
        text='As of today, no compensation has been paid.'
        a=anchors_for({1:text});a[0]['quoted']=True
        result=expand(dict(facts=[dict(anchorId=0,category='COMPENSATION_FACT',field='compensation',scope='Quoted')],needsReview=False),a,{1:text})
        self.assertEqual('Quoted',result['facts'][0]['scope'])
        text='In 2018 the position at present was that no compensation had been paid.'
        result=expand(dict(facts=[dict(anchorId=0,category='COMPENSATION_FACT',field='compensation',scope='Historical')],needsReview=False),anchors_for({1:text}),{1:text})
        self.assertEqual('Historical',result['facts'][0]['scope'])
    def test_compensation_predicate_not_generic_land(self):
        text='No compensation has been paid.'
        candidate=fact(text,category='LAND_FACT',field='land',actor=None,deadlineText=None)
        with self.assertRaisesRegex(ValueError,'Compensation-only'):validate(dict(facts=[candidate],needsReview=False),{1:text})
    def test_real_land_predicate_with_incidental_compensation(self):
        text='The land belongs to the owner named in the compensation register.'
        candidate=fact(text,category='LAND_FACT',field='land',actor=None,deadlineText=None)
        validate(dict(facts=[candidate],needsReview=False),{1:text})
    def test_unresolved_state_and_prayer_share_one_recovery(self):
        pages={1:'The petitioner seeks compensation. '+self.text};a=anchors_for(pages)
        bad=dict(facts=[dict(anchorId=a[-1]['anchorId'],category='LAND_FACT',field='land',scope='Uncertain')],needsReview=False)
        normal=expand(bad,a,pages,defer_semantic=True)
        self.assertEqual([],normal['facts']);self.assertTrue(normal['unresolvedAnchorIds'])
        class Model:
            calls=0
            def extract(self,instructions,source,schema,*args):
                self.calls+=1
                candidates=json.loads(source)['anchors']
                return dict(facts=[dict(anchorId=x['anchorId'],category='PETITIONER_SUBMISSION' if x.get('speechRole') else 'COMPENSATION_FACT',field='context' if x.get('speechRole') else 'compensation',scope='Current') for x in candidates],needsReview=False)
        model=Model();extra,audit=recover(a,normal['facts'],pages,'case','2026-01-01',model)
        self.assertEqual(1,model.calls);self.assertEqual(2,len(extra));self.assertEqual([],audit['remainingAnchorIds'])
    def test_failed_recovery_no_promotion(self):
        pages={1:self.text};a=anchors_for(pages)
        class Model:
            calls=0
            def extract(self,*args):
                self.calls+=1
                return dict(facts=[dict(anchorId=0,category='LAND_FACT',field='land',scope='Uncertain')],needsReview=False)
        model=Model();extra,audit=recover(a,[],pages,'case','2026-01-01',model)
        self.assertEqual(1,model.calls);self.assertEqual([],extra);self.assertTrue(audit['needsReview']);self.assertEqual([0],audit['remainingAnchorIds'])

if __name__=='__main__':unittest.main()
