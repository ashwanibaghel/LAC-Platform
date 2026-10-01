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
