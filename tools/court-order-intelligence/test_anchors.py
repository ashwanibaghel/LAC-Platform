import unittest
from anchors import anchors_for,expand,schema_for
from semantics import office_action
from test_worker import order

class AnchorTests(unittest.TestCase):
    def test_grammar_excludes_inadmissible_court_category(self):
        import jsonschema
        pages={1:'The petitioner submits compensation was not paid.'}
        anchors=anchors_for(pages)
        schema=schema_for(anchors,pages)
        with self.assertRaises(jsonschema.ValidationError):
            jsonschema.validate({'facts':[{'anchorId':0,'category':'COURT_FINDING','field':'compensation','scope':'Current'}],'needsReview':False},schema)

    def test_two_actor_passage_retains_only_explicit_subject(self):
        pages={1:'The LAC shall file a report after consulting DDA.'}
        result=expand({'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors_for(pages),pages)
        self.assertEqual('LAC',result['facts'][0]['actor'])

    def test_oversized_source_fails_closed_without_clipping(self):
        with self.assertRaises(ValueError): anchors_for({1:'The LAC '+('must retain every condition '*40)+'.'})

    def test_mult_page_quote_cannot_restart_old_direction(self):
        pages={1:'An earlier order reads as follows: “The LAC shall file a report.',2:'The LAC shall release compensation.” The Court directs parties to appear.'}
        anchors=anchors_for(pages)
        quoted=next(anchor for anchor in anchors if anchor['page']==2 and 'release' in anchor['text'])
        self.assertTrue(quoted['quoted'])
        with self.assertRaises(ValueError):
            expand({'facts':[{'anchorId':quoted['anchorId'],'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors,pages)

    def test_caption_is_context_not_selectable_evidence(self):
        pages={1:'Petitioner Through: Mr One Lawyer versus LAC Respondents Through: Mr Yeeshu Jain O R D E R Mr Yeeshu Jain states that the counter affidavit shall be filed within two days. Renotify on 25.07.2016.'}
        anchors=anchors_for(pages)
        self.assertFalse(any('Through:' in anchor['text'] for anchor in anchors))
        spoken=next(anchor for anchor in anchors if 'states' in anchor['text'])
        self.assertEqual('LAC_OR_RESPONDENT_SUBMISSION',spoken['speechRole'])
        with self.assertRaises(ValueError):
            expand({'facts':[{'anchorId':spoken['anchorId'],'category':'PETITIONER_SUBMISSION','field':'filing','scope':'Current'}],'needsReview':False},anchors,pages)

    def test_fresh_renotify_after_submission_is_operative(self):
        import jsonschema
        pages={1:'Mr Yeeshu Jain states that a report will be filed. Renotify on 25.07.2016.'}
        anchors=anchors_for(pages)
        renotify=next(anchor for anchor in anchors if 'Renotify' in anchor['text'])
        result={'facts':[{'anchorId':renotify['anchorId'],'category':'COURT_DIRECTION','field':'nextHearing','scope':'Current'}],'needsReview':False}
        jsonschema.validate(result,schema_for(anchors,pages))
        self.assertEqual('Current',expand(result,anchors,pages)['facts'][0]['scope'])

    def test_deadline_retains_preference_and_dotted_date(self):
        for text,expected in [('The LAC shall forward the reference preferably within four weeks from today.','preferably within four weeks from today'),('The LAC shall file the report by 28.02.2026.','by 28.02.2026')]:
            pages={1:text}
            result=expand({'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors_for(pages),pages)
            self.assertEqual(expected,result['facts'][0]['deadlineText'])

    def test_setting_aside_prayer_decision_does_not_hide_fresh_lac_direction(self):
        pages={1:'The order declining the petitioner’s prayer is set aside. The LAC is directed to forward the reference preferably within four weeks from today.'}
        anchors=anchors_for(pages)
        selected=next(anchor for anchor in anchors if 'is directed' in anchor['text'])
        result=expand({'facts':[{'anchorId':selected['anchorId'],'category':'COURT_DIRECTION','field':'referenceToAdj','scope':'Current'}],'needsReview':False},anchors,pages)
        self.assertIsNotNone(office_action(result['facts'][0],order()))

    def test_nested_quote_does_not_promote_old_direction(self):
        pages={1:'The previous order reads: “The term “compensation” is recorded. The LAC shall file a report.” Renotify on 25.07.2016.'}
        anchors=anchors_for(pages)
        old=next(anchor for anchor in anchors if 'shall file' in anchor['text'])
        self.assertTrue(old['quoted'])
        with self.assertRaisesRegex(ValueError,'source quotation'):
            expand({'facts':[{'anchorId':old['anchorId'],'category':'COURT_DIRECTION','field':'filing','scope':'Current'}],'needsReview':False},anchors,pages)

    def test_indexed_source_keeps_exact_page_and_text(self):
        pages={3:'1. The LAC shall file a status report within four weeks from today.'}
        anchors=anchors_for(pages)
        selected={'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False}
        result=expand(selected,anchors,pages)
        self.assertEqual(3,result['facts'][0]['page'])
        self.assertIn(result['facts'][0]['evidence'],pages[3])
        self.assertIsNotNone(office_action(result['facts'][0],order()))

    def test_forged_anchor_rejected(self):
        with self.assertRaises(ValueError):expand({'facts':[{'anchorId':99,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors_for({1:'The LAC shall file a status report.'}),{1:'The LAC shall file a status report.'})

    def test_actor_jurisdiction_kept_safe(self):
        pages={1:'The LAC Faridabad shall release compensation.'}
        anchors=anchors_for(pages)
        result=expand({'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors,pages)
        self.assertIsNone(office_action(result['facts'][0],order()))

    def test_submission_context_cannot_be_dropped(self):
        pages={1:'Counsel for petitioner submits that: The LAC shall release compensation.'}
        anchors=anchors_for(pages)
        with self.assertRaises(ValueError):expand({'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors,pages)

if __name__=='__main__':unittest.main()
