import unittest
from anchors import anchors_for
from completeness import recover
from semantics import synthesize, usable_facts, conflicting_fact_indexes, proposition_kind
from test_worker import fact, order


class CorrectnessTests(unittest.TestCase):
    def state(self,text,**overrides):
        return fact(text,category='COMPENSATION_FACT',field='compensation',actor=None,deadlineText=None,**overrides)

    def test_present_state_and_future_permission_coexist(self):
        facts=[self.state('No compensation has been paid.'),self.state('After examining the documents, the LAC may release compensation.')]
        self.assertEqual(set(),conflicting_fact_indexes(facts))
        data=synthesize('id','case',[order(facts)])
        self.assertEqual(2,len(data['latestOrder']['summaryFacts']))
        self.assertEqual(1,len(data['conditionalDirections']))
        self.assertEqual([],data['actions'])

    def test_opposed_payment_states_are_withheld(self):
        facts=[self.state('No compensation has been paid.'),self.state('Compensation has been paid.')]
        self.assertEqual({0,1},conflicting_fact_indexes(facts))
        self.assertEqual([],usable_facts(order(facts)))

    def test_explicit_court_finding_conflicts_with_same_recorded_state(self):
        facts=[self.state('No compensation has been paid.'),
               fact('The Court found that compensation has already been paid.',category='COURT_FINDING',field='finding',actor=None,deadlineText=None)]
        self.assertEqual({0,1},conflicting_fact_indexes(facts))
        self.assertEqual([],usable_facts(order(facts)))

    def test_conditional_reference_and_no_reference_coexist(self):
        facts=[fact('No reference has been made.',category='REFERENCE_FACT',field='referenceToAdj',actor=None,deadlineText=None),
               fact('If the need arises, the LAC to make a reference.',category='PROCEDURAL_EVENT',field='referenceToAdj',deadlineText=None)]
        data=synthesize('id','case',[order(facts)])
        self.assertEqual(2,len(data['latestOrder']['summaryFacts']))
        self.assertEqual(1,len(data['conditionalDirections']))
        self.assertEqual([],data['actions'])

    def test_historical_direction_does_not_become_current_action(self):
        data=synthesize('id','case',[order([fact(scope='Historical'),self.state('No compensation has been paid.')])])
        self.assertEqual([],data['actions'])

    def test_permission_never_becomes_mandatory(self):
        permission=fact('The LAC may release compensation.',deadlineText=None)
        self.assertEqual('Permission',proposition_kind(permission))
        self.assertEqual([],synthesize('id','case',[order([permission])])['actions'])

    def test_condition_on_shall_is_not_unconditional_action(self):
        candidate=fact('The LAC shall release payment if the claimant proves title.',deadlineText=None)
        self.assertEqual('ConditionalDirection',proposition_kind(candidate))
        self.assertEqual([],synthesize('id','case',[order([candidate])])['actions'])

    def test_descriptive_state_coexists_with_mandatory_future_direction(self):
        facts=[self.state('No compensation has been paid.'),
               fact('The LAC shall pay compensation.',deadlineText=None)]
        self.assertEqual(set(),conflicting_fact_indexes(facts))
        self.assertEqual(2,len(usable_facts(order(facts))))

    def test_different_predicate_time_and_scope_do_not_conflict(self):
        for other in [self.state('Compensation has been deposited.'),
                      self.state('Compensation was paid on 1 January 2020.'),
                      self.state('Compensation has been paid.',scope='Historical')]:
            self.assertEqual(set(),conflicting_fact_indexes([self.state('No compensation has been paid.'),other]))

    def test_different_record_identifiers_and_actors_do_not_conflict(self):
        facts=[self.state('Compensation for parcel 12 was paid.'),self.state('Compensation for parcel 13 was not paid.')]
        self.assertEqual(set(),conflicting_fact_indexes(facts))

    def test_semantic_kinds_do_not_create_or_relabel_facts(self):
        samples=[(self.state('Compensation has not been paid.'),'DescriptiveState'),
                 (fact(),'MandatoryDirection'),
                 (fact('If required, the LAC shall file the reference.'),'ConditionalDirection'),
                 (fact('The LAC may file the reference.'),'Permission'),
                 (fact('The petitioner submits payment is due.',category='PETITIONER_SUBMISSION'),'Submission'),
                 (fact('The directed status report has been filed.',category='RECORDED_COMPLIANCE'),'RecordedCompliance')]
        for candidate,expected in samples:
            original=dict(candidate)
            self.assertEqual(expected,proposition_kind(candidate))
            self.assertEqual(original,candidate)

    def test_targeted_review_flag_is_not_lost(self):
        pages={1:'The petitioner seeks compensation.'}
        class Model:
            def extract(self,*args):
                return dict(facts=[dict(anchorId=0,category='PETITIONER_SUBMISSION',field='context',scope='Current')],needsReview=True)
        extra,audit=recover(anchors_for(pages),[],pages,'case','2026-01-01',Model())
        self.assertTrue(extra); self.assertTrue(audit['needsReview'])

    def test_omitted_relief_uses_exactly_one_model_call(self):
        pages={1:'The present petition has been filed by the Petitioner seeking directions to pay compensation.'}
        anchors=anchors_for(pages)
        class Model:
            calls=0
            def extract(self,*args):
                self.calls+=1
                return dict(facts=[dict(anchorId=0,category='PETITIONER_SUBMISSION',field='context',scope='Current')],needsReview=False)
        model=Model(); extra,audit=recover(anchors,[],pages,'case','2026-01-01',model)
        self.assertEqual(1,model.calls); self.assertTrue(audit['used'])
        self.assertEqual('PETITIONER_SUBMISSION',extra[0]['category'])
        self.assertEqual([],audit['remainingAnchorIds'])

    def test_failed_targeted_call_does_not_manufacture_relief(self):
        pages={1:'The petitioner seeks compensation.'}
        class Model:
            calls=0
            def extract(self,*args): self.calls+=1; raise ValueError('Invalid response')
        model=Model(); extra,audit=recover(anchors_for(pages),[],pages,'case','2026-01-01',model)
        self.assertEqual([],extra); self.assertEqual(1,model.calls)
        self.assertTrue(audit['remainingAnchorIds'])

    def test_conditional_release_and_reference_never_create_deadline_action(self):
        texts=['After examining the documents, the LAC may release the compensation.',
               'Thereafter, if the need arises, the LAC to make a reference under Section 30 and 31 and the same be adjudicated within a period of six months.']
        facts=[fact(text,category='PROCEDURAL_EVENT',field='referenceToAdj',deadlineText=None) for text in texts]
        data=synthesize('id','case',[order(facts)])
        self.assertEqual([],data['beforeNextHearing']); self.assertEqual([],data['actions'])
        self.assertEqual(2,len(data['conditionalDirections']))
        for entry in data['conditionalDirections']:
            self.assertNotIn('dueDate',entry); self.assertNotIn('deadlineText',entry)
            self.assertEqual('Not established',entry['completion'])
        self.assertEqual(texts[1],data['conditionalDirections'][1]['source']['evidence'])

    def test_party_and_historical_conditions_are_not_court_directions(self):
        text='If required, the LAC may release compensation.'
        data=synthesize('id','case',[order([fact(text,category='LAC_OR_RESPONDENT_SUBMISSION',field='context'),
                                          fact(text,category='PROCEDURAL_EVENT',field='context',scope='Quoted')])])
        self.assertEqual([],data['conditionalDirections'])
