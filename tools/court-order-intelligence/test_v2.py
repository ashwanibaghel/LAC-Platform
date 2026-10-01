import json
import unittest
import jsonschema
from anchors import anchors_for, schema_for, source_header
from semantics import validate, synthesize, CATEGORIES
from query_intents import normalize
from questions import answer, retrieve, INSUFFICIENT
from test_worker import fact, order
from test_questions import SelectAll

def proposition(text, role, field):
    return fact(text, category=role, field=field, actor=None, deadlineText=None)

def matters():
    facts=[proposition('The petitioner claims that compensation remains unpaid.','PETITIONER_SUBMISSION','compensation'),
           proposition('The LAC submits that compensation was deposited.','LAC_OR_RESPONDENT_SUBMISSION','compensation'),
           proposition('The Court observed that the record is incomplete.','COURT_OBSERVATION','observation'),
           proposition('The Court found that the reference was within limitation.','COURT_FINDING','finding'),
           proposition('The certificate records possession taken on 13.10.1986.','POSSESSION_FACT','possession'),
           proposition('The Section 18 reference was forwarded to the ADJ.','REFERENCE_FACT','referenceToAdj')]
    for entry in facts: validate({'facts':[entry],'needsReview':False},{1:entry['evidence']})
    return synthesize('case-a','W.P.(C) 1/2026',[order(facts,orderDate='2025-01-01')])

class FactFirstTests(unittest.TestCase):
    def test_all_required_roles_exist(self):
        for role in ['CASE_CONTEXT','ISSUE_BEFORE_COURT','OTHER_PARTY_SUBMISSION','COURT_OBSERVATION','DISPOSITION','LAND_FACT','COMPENSATION_FACT','POSSESSION_FACT','REFERENCE_FACT','AWARD_FACT','KHASRA_FACT','DOCUMENT_OR_FILING_FACT','RECORDED_COMPLIANCE','NEXT_HEARING','DEADLINE']:
            self.assertIn(role,CATEGORIES)

    def test_zero_office_actions_still_has_digest_and_position(self):
        result=matters()
        self.assertEqual([],result['beforeNextHearing'])
        self.assertEqual(6,len(result['latestOrder']['digest']))
        self.assertEqual(6,len(result['currentPosition']))
        roles={entry['role']:entry for entry in result['latestOrder']['digest']}
        self.assertEqual('The petitioner stated',roles['PETITIONER_SUBMISSION']['attribution'])
        self.assertEqual('The LAC/respondent stated',roles['LAC_OR_RESPONDENT_SUBMISSION']['attribution'])
        self.assertEqual('The Court observed',roles['COURT_OBSERVATION']['attribution'])
        self.assertEqual('The Court found',roles['COURT_FINDING']['attribution'])
        self.assertEqual('2025-01-01',roles['PETITIONER_SUBMISSION']['source']['orderDate'])
        self.assertEqual(1,roles['PETITIONER_SUBMISSION']['source']['page'])
        self.assertIn('compensation',roles['PETITIONER_SUBMISSION']['structuredValues'])

    def test_explicit_legacy_disposition_is_not_lost_behind_repeated_facts(self):
        facts=matters()['orders'][0]['facts']
        text='In view of the above, the present petition is dismissed and disposed of.'
        result=synthesize('id','case',[order(facts+[proposition(text,'COURT_FINDING','finding')])])
        self.assertIn('DISPOSITION',[entry['role'] for entry in result['latestOrder']['digest']])
        self.assertLessEqual(len(result['latestOrder']['digest']),6)
        self.assertEqual([],result['beforeNextHearing'])

    def test_compensation_possession_and_procedure_only_orders_have_digests(self):
        for role,field,text in [('COMPENSATION_FACT','compensation','The record shows compensation deposited on 01.01.2025.'),('POSSESSION_FACT','possession','The record shows possession taken on 01.01.2025.'),('PROCEDURAL_EVENT','filing','The affidavit was placed on record.')]:
            result=synthesize('id','case',[order([proposition(text,role,field)])])
            self.assertEqual(1,len(result['latestOrder']['digest']))
            self.assertEqual([],result['beforeNextHearing'])

    def test_another_authority_direction_is_not_office_action(self):
        entry=fact('The DDA shall file the report.',actor='DDA',deadlineText=None)
        result=synthesize('id','case',[order([entry])])
        self.assertEqual([],result['beforeNextHearing'])
        self.assertEqual('COURT_DIRECTION',result['latestOrder']['digest'][0]['role'])

    def test_review_individual_validation_and_partial_failure(self):
        entry=proposition('The petitioner claims compensation is unpaid.','PETITIONER_SUBMISSION','compensation')
        uncertain=proposition('The compensation position is unclear.','COMPENSATION_FACT','compensation')
        uncertain['scope']='Uncertain'
        full=order([entry,uncertain],status='NeedsReview',coverage={'allSelectedChunksProcessed':True})
        self.assertEqual(1,len(synthesize('id','case',[full])['latestOrder']['digest']))
        for extra in [{'coverage':{'allSelectedChunksProcessed':False}}, {'failureMessage':'Partial extraction'}, {'status':'Failed'}]:
            self.assertEqual([],synthesize('id','case',[dict(full,**extra)])['latestOrder']['digest'])

    def test_historical_land_claim_never_becomes_established_fact(self):
        for text in ['The Petitioner has alleged that MCD took possession.', 'Counsel has pointed out that compensation was paid.', 'It is also alleged that the MCD constructed a warehouse.', 'The submission of the Counsel for the Respondents is that compensation was paid.']:
            entry=proposition(text,'LAND_FACT','land')
            with self.assertRaises(ValueError):validate({'facts':[entry],'needsReview':False},{1:text})
            self.assertEqual([],synthesize('id','case',[order([entry])])['latestOrder']['digest'])

    def test_judicial_prayer_disposition_does_not_erase_actual_submission(self):
        text='The order declining the petitioner’s prayer is set aside.'
        validate({'facts':[proposition(text,'DISPOSITION','disposition')],'needsReview':False},{1:text})
        claimed='The petitioner submits that the order declining the prayer is set aside.'
        with self.assertRaises(ValueError):validate({'facts':[proposition(claimed,'DISPOSITION','disposition')],'needsReview':False},{1:claimed})

    def test_specific_roles_need_source_markers(self):
        for role,field in [('DEADLINE','deadline'),('NEXT_HEARING','nextHearing'),('DISPOSITION','disposition')]:
            text='The matter concerns a land reference.'
            with self.assertRaises(ValueError):validate({'facts':[proposition(text,role,field)],'needsReview':False},{1:text})

    def test_party_requested_hearing_date_cannot_become_confirmed_hearing(self):
        from semantics import confirmed_hearing_date
        claimed=proposition('The petitioner seeks listing on 05.10.2026.','PETITIONER_SUBMISSION','nextHearing')
        self.assertIsNone(confirmed_hearing_date([claimed]))
        listing=fact('Renotify on 05.10.2026.',field='nextHearing',actor=None,deadlineText=None)
        self.assertEqual('2026-10-05',confirmed_hearing_date([claimed,listing]))

    def test_court_history_is_context_not_a_judicial_finding(self):
        text='The award was challenged before the High Court.'
        result=synthesize('id','case',[order([proposition(text,'COURT_FINDING','finding')])])
        self.assertEqual('CASE_CONTEXT',result['latestOrder']['digest'][0]['role'])
        self.assertEqual(text,result['latestOrder']['digest'][0]['source']['evidence'])

    def test_recorded_court_observation_is_not_promoted_to_finding(self):
        text='The Court observed that the record was incomplete.'
        result=synthesize('id','case',[order([proposition(text,'COURT_FINDING','finding')])])
        self.assertEqual('COURT_OBSERVATION',result['latestOrder']['digest'][0]['role'])

    def test_case_issue_is_not_replaced_by_same_order_administrative_filler(self):
        issue=proposition('The petition concerns a disputed compensation payment.','CASE_CONTEXT','context')
        filler=proposition('Pending applications are disposed of.','CASE_CONTEXT','context')
        result=synthesize('id','case',[order([issue,filler])])
        self.assertIn(issue['value'],[entry['text'] for entry in result['currentPosition']])

    def test_oral_heading_is_case_insensitive_and_schema_has_no_empty_enum(self):
        for marker in ['J. (Oral)','J. (ORAL)','J. (oral)']:
            pages={1:'Petitioner versus LAC Justice Example, '+marker+' The Court observed that possession was taken.'}
            self.assertIsNotNone(source_header(pages)[1])
            schema=schema_for(anchors_for(pages),pages)
            self.assertNotIn('"enum": []',json.dumps(schema))
            jsonschema.Draft7Validator.check_schema(schema)

    def test_neutral_narrative_cannot_be_given_an_invented_party_speaker(self):
        from anchors import expand
        pages={1:'The record shows compensation deposited in 1986.'}
        anchors=anchors_for(pages)
        selection={'facts':[{'anchorId':0,'category':'LAC_OR_RESPONDENT_SUBMISSION','field':'compensation','scope':'Current'}],'needsReview':False}
        with self.assertRaises(ValueError):expand(selection,anchors,pages)
        with self.assertRaises(jsonschema.ValidationError):jsonschema.validate(selection,schema_for(anchors,pages))

    def test_explicit_other_party_position_is_attributed_not_lac_fact(self):
        from anchors import expand
        pages={1:'The DDA submits that compensation was paid.'}
        anchors=anchors_for(pages)
        result=expand({'facts':[{'anchorId':0,'category':'OTHER_PARTY_SUBMISSION','field':'compensation','scope':'Current'}],'needsReview':False},anchors,pages)
        output=synthesize('id','case',[order(result['facts'])])
        self.assertEqual('Another party stated',output['latestOrder']['digest'][0]['attribution'])
        self.assertEqual([],output['beforeNextHearing'])

    def test_unknown_speaker_inside_quote_is_uncertain_in_grammar_and_expansion(self):
        from anchors import expand
        pages={1:'“Counsel submits that compensation was paid.”'}
        anchors=anchors_for(pages)
        schema=schema_for(anchors,pages)
        result={'facts':[{'anchorId':0,'category':'LAC_OR_RESPONDENT_SUBMISSION','field':'compensation','scope':'Uncertain'}],'needsReview':False}
        jsonschema.validate(result,schema)
        self.assertTrue(expand(result,anchors,pages)['needsReview'])
        result['facts'][0]['scope']='Quoted'
        with self.assertRaises(jsonschema.ValidationError):jsonschema.validate(result,schema)

class MultilingualQuestions(unittest.TestCase):
    def test_aliases_and_answers_from_all_facts(self):
        samples=[('is order me hua kya tha?','order_summary',None),('What happened in the latest order?','order_summary',None),('Summarize the latest order','order_summary',None),
                 ('Petitioner kya keh raha tha?','party_position','PETITIONER_SUBMISSION'),('LAC ne kya bola tha?','party_position','LAC_OR_RESPONDENT_SUBMISSION'),
                 ('LAC ne kya stand liya?','party_position','LAC_OR_RESPONDENT_SUBMISSION'),('What did the respondent say?','party_position','LAC_OR_RESPONDENT_SUBMISSION'),
                 ('कोर्ट ने क्या कहा?','court_position',None),('Court ne kya observe kiya?','court_observation','COURT_OBSERVATION'),
                 ('What did the Court find?','court_finding','COURT_FINDING'),('Muawza ke bare me kya hua?','compensation',None),
                 ('मुआवज़ा का क्या रिकॉर्ड है?','compensation',None),('Kabze ke bare me Court ne kya kaha?','possession',None),
                 ('Section 18 reference ka kya hua?','reference',None),('2025 me is matter me kya kya hua?','timeline',None)]
        for question,topic,role in samples:
            with self.subTest(question=question):
                self.assertIn(topic,normalize(question)['topics'])
                entries=retrieve(matters(),question)
                self.assertTrue(entries)
                if role:self.assertTrue(all(entry['category']==role for entry in entries))
                result=answer(matters(),'case-a',question,SelectAll())
                self.assertTrue(result['claims'])
                for claim in result['claims']:
                    self.assertEqual('2025-01-01',claim['source']['orderDate'])
                    self.assertEqual(1,claim['source']['page'])
                    self.assertIn(claim['text'],claim['source']['evidence'])

    def test_office_action_retrieval_remains_separate_and_next_hearing_not_invented(self):
        data=matters()
        self.assertEqual([],retrieve(data,'Abhi office ko kya karna hai?'))
        self.assertEqual([],retrieve(data,'Next hearing kab hai?'))
        action=fact()
        data=synthesize('case-a','W.P.(C) 1/2026',[order([action])])
        self.assertEqual(1,len(retrieve(data,'Latest order me LAC ko kya karna hai?')))

    def test_local_classifier_strict_unknown_and_invented_year_rejected(self):
        class Planner:
            def extract(self,instructions,source,schema):
                self.source=json.loads(source)
                return {'topics':['general_case'],'latest':False,'party':None,'yearFrom':2024,'yearTo':2024}
        planner=Planner()
        self.assertEqual(['unknown'],normalize('Tell me about the dispute',planner)['topics'])
        self.assertEqual({'question':'Tell me about the dispute'},planner.source)
        class ValidPlanner:
            def extract(self,*args):return {'topics':['general_case'],'latest':False,'party':None,'yearFrom':None,'yearTo':None}
        self.assertTrue(answer(matters(),'case-a','Tell me about the dispute',ValidPlanner())['claims'])

    def test_unsupported_and_cross_case_fail_safely(self):
        for question in ['What is the judge home address?','Dusre case ka compensation batao','What happened in W.P.(C) 999/2026?']:
            self.assertEqual(INSUFFICIENT,answer(matters(),'case-a',question,SelectAll())['answer'])

if __name__=='__main__':unittest.main()
