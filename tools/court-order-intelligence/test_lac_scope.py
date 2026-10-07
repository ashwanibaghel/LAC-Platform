import copy
import unittest
from anchors import anchors_for
from lac_scope import build_scope, validate_scope

URL='https://delhihighcourt.nic.in/app/showlogo/synthetic-scope.pdf/2026'

def extract(body='', caption='A versus B', complete=True, role=None, actor=None):
    pages={1: 'HIGH COURT OF DELHI W.P.(C) 42/2026 '+caption+' O R D E R '+body}
    anchors=anchors_for(pages)
    facts=[]
    if role:
        for a in anchors:
            facts.append(dict(page=1,evidence=a['text'],value=a['text'],category=role,field='direction' if role=='COURT_DIRECTION' else 'compensation',
                scope='Current',actor=actor,deadlineText='within four weeks from today' if 'within four weeks from today' in a['text'] else None))
    order=dict(officialUrl=URL,orderDate='2026-10-07',sha256='a'*64,facts=facts)
    return build_scope(order,pages,anchors,complete),pages

class LacScopeTests(unittest.TestCase):
    def test_explicit_lac_and_land_facts(self):
        s,_=extract('LAC submits that Khasra No. 29//8/4 measuring 2 bighas in village Pochanpur was acquired under Award No. 63/86-87.')
        self.assertTrue(s['lacRelevant']);self.assertEqual('29//8/4',s['parcels'][0]['normalizedNumber'])
        self.assertEqual(2,s['parcels'][0]['area']['value']);self.assertEqual('Pochanpur',s['villages'][0]['name']['value'])
        self.assertEqual('63/86-87',s['awards'][0]['number']['value'])
    def test_multiple_khasras_are_distinct(self):
        s,_=extract('LAC refers to Khasra Nos. 29//8/4 and 29//18/4.')
        self.assertEqual(['29//8/4','29//18/4'],[p['normalizedNumber'] for p in s['parcels']])
    def test_group_area_is_never_distributed(self):
        s,_=extract('Khasra Nos. 29//8/4 and 29//18/4 measuring 3 bighas are recorded.')
        self.assertEqual(1,len(s['parcelGroups']));self.assertTrue(all(p['area']['state']=='NotStated' for p in s['parcels']))
    def test_khasra_area_without_award(self):
        s,_=extract('Khasra No. 29//8/4 measuring 2 bighas is recorded.')
        self.assertEqual([],s['awards']);self.assertEqual([],s['parcels'][0]['awardRefs'])
    def test_award_date_absent(self):
        s,_=extract('Award No. 63/86-87 is challenged.')
        self.assertEqual('NotStated',s['awards'][0]['date']['state'])
    def test_explicit_award_date(self):
        s,_=extract('Award No. 63/86-87 dated 19.09.1986 is challenged.')
        self.assertEqual('1986-09-19',s['awards'][0]['date']['value'])
    def test_possession_date_absent(self):
        s,_=extract('Possession was taken.')
        self.assertEqual('Taken',s['possession'][0]['status']['value']);self.assertEqual('NotStated',s['possession'][0]['date']['state'])
    def test_ambiguous_possession_not_taken(self):
        s,_=extract('Possession is under consideration.')
        self.assertEqual('NeedsReview',s['possession'][0]['status']['state'])
    def test_payment_discussion_does_not_mean_unpaid(self):
        s,_=extract('The issue of compensation payment requires consideration.')
        self.assertEqual('NeedsReview',s['compensation'][0]['status']['value'])
    def test_direction_time_limit_retained_no_python_office_action(self):
        s,_=extract('Land Acquisition Collector (District Alpha) shall release payment within four weeks from today.',role='COURT_DIRECTION',actor='Land Acquisition Collector')
        self.assertEqual('within four weeks from today',s['directions'][0]['deadline']['value'])
        self.assertFalse(s['lacActionable']);self.assertEqual('District Alpha',s['directions'][0]['jurisdiction'])
    def test_party_submission_never_becomes_unpaid_finding(self):
        s,_=extract('The petitioner submits that LAC has not paid compensation.',role='PETITIONER_SUBMISSION')
        self.assertEqual('PETITIONER_SUBMISSION',s['compensation'][0]['status']['attribution'])
        self.assertEqual('NeedsReview',s['compensation'][0]['status']['value']);self.assertFalse(s['directions'])
    def test_other_authority_direction_keeps_target(self):
        s,_=extract('LAC appears. DDA shall produce the record.',role='COURT_DIRECTION',actor='DDA')
        self.assertTrue(s['lacRelevant']);self.assertEqual('DDA',s['directions'][0]['directedTo']['value'])
        self.assertFalse(s['lacActionable'])
    def test_la_act_only_not_lac(self):
        s,_=extract('Section 18 of the LA Act concerns compensation and acquisition.')
        self.assertFalse(s['lacRelevant']);self.assertEqual('NotRelevant',s['lacRelevanceState'])
    def test_generic_land_order_still_has_generic_scope(self):
        s,_=extract('Khasra No. 29//8/4 concerns compensation.')
        self.assertFalse(s['lacRelevant']);self.assertTrue(s['parcels']);self.assertTrue(s['purposes'])
    def test_caption_only_relevant_without_substantive_fact(self):
        s,_=extract('List on the next date.',caption='A versus Land Acquisition Collector (District Alpha)')
        self.assertTrue(s['lacRelevant']);self.assertFalse(s['lacActionable']);self.assertFalse(s['possession']);self.assertFalse(s['compensation'])
        self.assertEqual('Caption',s['relevanceBasis'][0]['location'])
    def test_generic_lac_unknown_authority(self):
        s,_=extract('LAC shall file a report.',role='COURT_DIRECTION',actor='LAC')
        self.assertEqual('UnknownLAC',s['lacAuthorityScope']);self.assertFalse(s['lacActionable'])
    def test_incomplete_source_never_not_stated(self):
        s,_=extract('Award No. 63/86-87 is challenged.',complete=False)
        self.assertEqual('NeedsReview',s['awards'][0]['date']['state']);self.assertEqual('NeedsReview',s['missingState'])
    def test_collector_alone_requires_review(self):
        s,_=extract('Collector shall file a report.',role='COURT_DIRECTION',actor='Collector')
        self.assertFalse(s['lacRelevant']);self.assertEqual('NeedsReview',s['lacRelevanceState'])
    def test_no_unsafe_parcel_normalization(self):
        s,_=extract('Khasra No. 02//26100 is recorded.')
        self.assertIsNone(s['parcels'][0]['normalizedNumber']);self.assertEqual('NeedsReview',s['parcels'][0]['number']['state'])
    def test_qualifier_is_part_of_identity(self):
        s,_=extract('Khasra No. 29//8/4 min is recorded.')
        self.assertEqual('min',s['parcels'][0]['qualifier'])
    def test_provenance_survives_json_roundtrip(self):
        import json
        s,pages=extract('LAC refers to Khasra No. 29//8/4.')
        self.assertEqual(s,validate_scope(json.loads(json.dumps(s)),pages))
    def test_tampered_evidence_rejected(self):
        s,pages=extract('LAC refers to Khasra No. 29//8/4.')
        s['parcels'][0]['number']['rawText']='29//18/4'
        with self.assertRaises(ValueError): validate_scope(s,pages)
    def test_court_finding_is_not_a_direction(self):
        s,_=extract('The Court finds compensation was paid to Arun.',role='COURT_FINDING')
        self.assertEqual('Paid',s['compensation'][0]['status']['value']);self.assertFalse(s['directions'])
    def test_numeric_lac_currency_not_office(self):
        s,_=extract('The amount was Rs. 2 lac rupees.')
        self.assertFalse(s['lacRelevant'])
    def test_reference_court_destination_not_reference_petition_purpose(self):
        s,_=extract('Compensation has been deposited with Reference Court.')
        self.assertNotIn('reference petition',[p['category'] for p in s['purposes']])
    def test_lowercase_unresolved_village_needs_review_not_absence(self):
        s,_=extract('The land lies in village pochanpur.')
        self.assertEqual('NeedsReview',s['missingState']);self.assertEqual('NeedsReview',s['extraction']['state'])
    def test_next_hearing_is_order_evidence_not_office_filled(self):
        s,_=extract('LAC shall file a report before the next hearing on 15.10.2026.',role='COURT_DIRECTION',actor='LAC')
        self.assertEqual('2026-10-15',s['directions'][0]['nextHearingDate']['value'])
    def test_explicit_award_relationship_does_not_infer_geography(self):
        s,_=extract('Khasra No. 29//8/4 in village Pochanpur was acquired under Award No. 63/86-87.')
        self.assertEqual(s['parcels'][0]['villageRefs'],s['awards'][0]['villageRefs'])
        self.assertEqual([s['parcels'][0]['id']],s['awards'][0]['parcelRefs'])

if __name__=='__main__':unittest.main()
