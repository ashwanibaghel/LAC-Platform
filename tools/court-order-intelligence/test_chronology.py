import unittest
from anchors import anchors_for, schema_for, expand
from chronology import caption_context
from semantics import synthesize
from semantics import validate, confirmed_hearing_date
from questions import retrieve
from query_intents import normalize
from test_worker import fact, order


class ChronologyTests(unittest.TestCase):
    def test_low_quote_defined_term_does_not_quote_remaining_judgment(self):
        pages={1:'1. The petition concerns the statute (hereinafter called „the 2013 Act‟). 2. The Award was made on 24.10.2002. 3. In that decision, this Court observed as under:- “8. The respondents submit possession was taken.” 4. As a consequence, compensation has not been paid. 5. The writ petition is allowed.'}
        anchors=anchors_for(pages)
        self.assertFalse(next(a for a in anchors if 'Award was' in a['text'])['quoted'])
        self.assertTrue(next(a for a in anchors if 'respondents submit' in a['text'])['quoted'])
        self.assertFalse(next(a for a in anchors if 'compensation has' in a['text'])['quoted'])
        self.assertFalse(next(a for a in anchors if 'petition is allowed' in a['text'])['quoted'])
    def test_caption_keeps_exact_advocate_block_with_page(self):
        text='W.P.(C) 22/2025 SHAMSHER SINGH .....Petitioner Through: Mr. A. K. Singh, Adv. versus GOVT OF NCT AND ANR .....Respondents Through: Mr. Rajneesh Sharma and Mr. Anil Pandey, Advs. for LAC. CORAM: HON\'BLE MR. JUSTICE SOMEONE'
        result=caption_context({1:text},'https://delhihighcourt.nic.in/app/test.pdf','2025-01-30')
        self.assertEqual('SHAMSHER SINGH vs GOVT OF NCT AND ANR',result['title'])
        self.assertIn('for LAC',result['respondentAdvocates']['text'])
        self.assertIn(result['respondentAdvocates']['source']['evidence'],text)
        self.assertEqual(1,result['bench']['source']['page'])

    def test_mentioned_dates_do_not_create_hearings(self):
        entry=fact('Compensation was collected on 01.08.2012 and reference filed on 13.08.2012.',category='CASE_CONTEXT',field='context',actor=None,deadlineText=None,scope='Historical')
        result=synthesize('id','case',[order([entry],orderDate='2025-01-30')])
        self.assertEqual(1,len(result['orders']))
        self.assertEqual(['2012-08-01','2012-08-13'],result['factualChronology'][0]['dates'])
        self.assertEqual('FactualReference',result['factualChronology'][0]['kind'])
        self.assertEqual('2025-01-30',result['factualChronology'][0]['source']['orderDate'])

    def test_quoted_precedent_dates_are_not_case_events(self):
        entry=fact('The LAC shall make payment by 01.01.2010.',scope='Quoted')
        self.assertEqual([],synthesize('id','case',[order([entry])])['factualChronology'])

    def test_final_outcome_preserves_all_previous_orders_and_gaps(self):
        entry=fact('The petition is allowed.',category='DISPOSITION',field='disposition',actor=None,deadlineText=None)
        result=synthesize('id','case',[order([fact()],orderDate='2024-01-01'),order([],status='NeedsSourceReview',orderDate='2024-03-01',failureMessage='Unavailable PDF'),order([entry],orderDate='2025-01-30')])
        self.assertEqual(3,len(result['orders']))
        self.assertEqual('2025-01-30',result['finalOrder']['orderDate'])
        self.assertEqual('Unavailable PDF',result['sourceCoverage']['gaps'][0]['reason'])

    def test_application_only_disposal_does_not_make_matter_final(self):
        entry=fact('The application is closed.',category='DISPOSITION',field='disposition',actor=None,deadlineText=None)
        self.assertIsNone(synthesize('id','case',[order([entry])])['finalOrder'])

    def test_last_three_hearings_preserve_middle_date(self):
        orders=[order([fact(f'The LAC shall file report {i}.',deadlineText=None)],orderDate=f'2025-0{i}-01') for i in range(1,5)]
        artifact=synthesize('id','case',orders)
        found=retrieve(artifact,'Last three hearings me kya hua?')
        self.assertEqual({'2025-02-01','2025-03-01','2025-04-01'},{entry['source']['orderDate'] for entry in found})
        self.assertEqual(3,normalize('Last three hearings me kya hua?')['lastOrderCount'])

    def test_replacement_quote_precedent_does_not_become_current(self):
        pages={1:'9. We extract the following passages from the said decision: �24. The LAC shall release compensation.',2:'25. The Collector shall forward the reference.� 10. In view of the above, the impugned order is set aside.'}
        anchors=anchors_for(pages)
        self.assertTrue(next(a for a in anchors if 'Collector shall' in a['text'])['quoted'])
        # Source boundary is fail closed if damaged text cannot establish the return.
        self.assertFalse(next(a for a in anchors if a['text'].startswith('In view'))['quoted'])

    def test_counter_affidavit_attribution_retained(self):
        pages={1:'5. The LAC has filed a counter affidavit affirming that notice was duly served upon the petitioner.'}
        anchors=anchors_for(pages)
        self.assertEqual('LAC_OR_RESPONDENT_SUBMISSION',anchors[0]['speechRole'])

    def test_cross_page_direction_retains_both_exact_fragments(self):
        pages={6:'24. The Respondents shall place on record the details of',7:'the notification and compensation that has been deposited. 25. List before Court on 28th September, 2026.'}
        anchors=anchors_for(pages)
        merged=anchors[0]
        self.assertEqual(2,len(merged['evidenceParts']))
        result=expand({'facts':[{'anchorId':0,'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False},anchors,pages)
        validate(result,pages)
        self.assertIn('compensation',result['facts'][0]['value'])
        result['facts'][0]['evidenceParts'][1]['page']=8
        with self.assertRaises(ValueError): validate(result,pages)

    def test_complete_or_non_adjacent_passages_are_not_joined(self):
        for pages in ({1:'The LAC shall file a report.',2:'The compensation is disputed.'}, {1:'The LAC shall file details of',3:'the compensation that was deposited.'}):
            self.assertEqual(2,len(anchors_for(pages)))

    def test_court_date_distinct_from_registrar_date(self):
        facts=[fact('List before the Joint Registrar on 27th July, 2026.',field='nextHearing',actor=None,deadlineText=None),fact('List before Court on 28th September, 2026.',field='nextHearing',actor=None,deadlineText=None)]
        self.assertEqual('2026-09-28',confirmed_hearing_date(facts))

    def test_future_counter_affidavit_direction_is_not_an_affidavit_claim(self):
        entry=fact('Counter affidavits, if any, be filed by all the Respondents within six weeks after service.',actor='Respondents',deadlineText='within six weeks after service')
        validate({'facts':[entry],'needsReview':False},{1:entry['evidence']})
        self.assertEqual([],synthesize('id','case',[order([entry])])['beforeNextHearing'])

    def test_missing_precedent_closing_glyph_returns_only_at_outer_paragraph(self):
        pages={1:'18. The Supreme Court in its decision observed as under: “52. The Collector shall release compensation.',2:'The Collector shall take possession. 19. The petitioners have filed no objections. 20. The Respondents shall place the record.'}
        anchors=anchors_for(pages)
        self.assertTrue(next(a for a in anchors if 'take possession' in a['text'])['quoted'])
        self.assertFalse(next(a for a in anchors if a['text'].startswith('19.'))['quoted'])
        self.assertFalse(next(a for a in anchors if 'Respondents shall' in a['text'])['quoted'])

    def test_other_party_submissions_do_not_become_court_compensation_fact(self):
        pages={1:'14. Mr. Goel, Counsel for NHAI has made the following submissions: (i) The compensation has already been deposited. (ii) The land vests with the Government.'}
        anchors=anchors_for(pages)
        self.assertTrue(all(a['speechRole']=='OTHER_PARTY_SUBMISSION' for a in anchors))

    def test_case_footer_removed_without_losing_cross_page_direction(self):
        pages={1:'The LAC shall place the details of W.P.(C) 1/2026 Page 1 of 2 This is a digitally signed order.',2:'the compensation deposited. List on 15.10.2026.'}
        anchors=anchors_for(pages)
        self.assertNotIn('Page 1 of',anchors[0]['text'])
        self.assertIn('details of the compensation',anchors[0]['text'])

    def test_last_opportunity_is_not_a_claim_or_completed_action(self):
        text='By way of last and final opportunity, four weeks time is granted to the respondents to file counter affidavit.'
        self.assertEqual([],synthesize('id','case',[order([fact(text,category='CASE_CONTEXT',field='context',actor='respondents',deadlineText=None)])])['beforeNextHearing'])

    def test_six_date_timeline_retains_every_date_and_does_not_invent_filing(self):
        from questions import answer
        class FirstOnly:
            def extract(self,*args): return {'claims':[{'factId':0}]}
        orders=[order([fact('The LAC shall file a status report.',deadlineText=None)],orderDate=f'2025-0{i}-01') for i in range(1,7)]
        artifact=synthesize('id','case',orders)
        result=answer(artifact,'id','2025 se ab tak kya hua?',FirstOnly())
        self.assertEqual(6,len({claim['source']['orderDate'] for claim in result['claims']}))
        self.assertEqual([],retrieve(artifact,'LAC ne kis date ko status report file ki?'))

    def test_explicit_counsel_statement_survives_model_omission_as_submission_only(self):
        pages={1:'Mr. Pathak, counsel for the LAC submits that similar matters are pending before this Court.'}
        anchors=anchors_for(pages)
        output=expand({'facts':[],'needsReview':False},anchors,pages)
        self.assertEqual(1,len(output['facts']))
        self.assertEqual('LAC_OR_RESPONDENT_SUBMISSION',output['facts'][0]['category'])
        self.assertIn(output['facts'][0]['evidence'],pages[1])

    def test_current_position_does_not_lead_with_old_adjournment(self):
        old=fact('Passover is sought on behalf of the Petitioner.',category='PETITIONER_SUBMISSION',field='context',actor=None,deadlineText=None)
        latest=fact('Respondents shall file counter affidavit.',actor=None,deadlineText=None)
        result=synthesize('id','case',[order([old],orderDate='2025-01-01'),order([latest],orderDate='2026-01-01')])
        self.assertFalse(any('Passover' in entry['text'] for entry in result['currentPosition']))
        self.assertTrue(any('Passover' in entry['text'] for entry in result['orders'][0]['propositions']))

    def test_uncertainty_in_a_claim_does_not_erase_explicit_source_speaker(self):
        pages={1:'The LAC has filed a counter affidavit affirming that notice may have been served.'}
        anchors=anchors_for(pages)
        result=expand({'facts':[{'anchorId':0,'category':'LAC_OR_RESPONDENT_SUBMISSION','field':'context','scope':'Uncertain'}],'needsReview':False},anchors,pages)
        self.assertEqual('LAC_OR_RESPONDENT_SUBMISSION',result['facts'][0]['category'])
        self.assertEqual('Current',result['facts'][0]['scope'])
        self.assertIn('may have been',result['facts'][0]['value'])

    def test_invalid_model_timeline_selection_still_covers_six_dates(self):
        from questions import answer
        class Invalid:
            def extract(self,*args): return {'claims':[{'factId':999}]}
        orders=[order([fact('The LAC shall file a report.',deadlineText=None)],orderDate=f'2025-0{i}-01') for i in range(1,7)]
        result=answer(synthesize('id','case',orders),'id','2025 se ab tak kya hua?',Invalid())
        self.assertEqual(6,len({entry['source']['orderDate'] for entry in result['claims']}))


if __name__=='__main__': unittest.main()
