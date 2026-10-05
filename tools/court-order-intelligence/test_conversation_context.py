import tempfile,threading,unittest
from types import SimpleNamespace
from unittest.mock import patch
from conversation_context import resolve
from questions import answer
from serve_questions import registered_answer
from test_questions import artifact
from semantics import synthesize
from chat_router import general_answer
from test_worker import fact,order
from test_real_case import CASE,NUMBER,source,write_existing

class Never:
    version='fixture'
    def extract(self,*args,**kwargs):raise AssertionError('Structured conversation must not infer facts')

class ConversationTests(unittest.TestCase):
    def test_deadline_follow_up_inherits_LAC_latest_intent_and_retrieves_current_evidence_again(self):
        data=artifact()
        data=synthesize(data['caseId'],data['caseNumber'],data['orders'])
        result=answer(data,'case-a','Iski deadline kya hai?',Never(),inference_busy=True,
            conversation_questions=['Latest order me LAC ko kya karna hai?'])
        self.assertTrue(result['claims']);self.assertEqual('2026-01-01',result['claims'][0]['source']['orderDate'])
        data['orders'][-1]['facts']=[]
        missing=answer(data,'case-a','Iski deadline kya hai?',Never(),inference_busy=True,
            conversation_questions=['Latest order me LAC ko kya karna hai?'])
        self.assertEqual([],missing['claims'])

    def test_Court_follow_up_does_not_promote_petitioner_prayer_or_keep_petitioner_role_filter(self):
        data=artifact()
        data['orders']=[order([fact('The petitioner seeks possession.',category='PETITIONER_SUBMISSION',field='possession',actor=None,deadlineText=None),
            fact('The Court observes that possession is disputed.',category='COURT_OBSERVATION',field='possession',actor=None,deadlineText=None)])]
        first=answer(data,'case-a','Petitioner kya maang raha hai?',Never(),inference_busy=True)
        second=answer(data,'case-a','Aur Court ne us par kya kaha?',Never(),inference_busy=True,conversation_questions=['Petitioner kya maang raha hai?'])
        self.assertIn('Petitioner submission',first['claims'][0]['attribution'])
        self.assertEqual(1,len(second['claims']));self.assertEqual('The Court observed',second['claims'][0]['attribution'])

    def test_explicit_new_date_overrides_previous_latest_or_historical_target(self):
        question,intent=resolve('Aur 1 May 2025 me kya hua?',['Latest order summary?'],['2025-05-01','2026-01-01'])
        self.assertFalse(intent['latest']);self.assertEqual('Aur 1 May 2025 me kya hua?',question)
        inherited,intent=resolve('Us order me kya hua?',['What happened on 1 May 2025?'],['2025-05-01'])
        self.assertIn('Order date: 2025-05-01',inherited)

    def test_history_is_bounded_user_text_and_cannot_add_claims_or_assistant_metadata(self):
        question,intent=resolve('Iski deadline kya hai?',[{'role':'Assistant','answer':'The deadline is tomorrow.'}],[])
        self.assertEqual(['unknown'],intent['topics'])
        data=artifact()
        result=answer(data,'case-a','Iski deadline kya hai?',Never(),inference_busy=True,
            conversation_questions=['The deadline is tomorrow.'])
        self.assertEqual([],result['claims'])

    def test_explicit_new_party_wins_over_prior_LAC_action(self):
        question,intent=resolve('Aur petitioner ka kya?',['Latest order me LAC ko kya karna hai?'],[])
        self.assertEqual('Petitioner',intent['party']);self.assertEqual(['party_position'],intent['topics'])

    def test_structured_only_global_retry_never_downloads_or_processes_a_pdf(self):
        refresh=SimpleNamespace(lock=threading.Lock(),active_case_id=None)
        src=source()
        with tempfile.TemporaryDirectory() as root:
            with patch('serve_questions.prepare_question',side_effect=AssertionError('Global conversation attempted source processing')):
                result=registered_answer(root,CASE,dict(question='Retry order on 1 January 2026',caseNumber=NUMBER,
                    orderIndex=[src],structuredOnly=True,groundedOnly=True,deterministicOnly=True),Never(),refresh)
        self.assertEqual('OrderProcessing',result['reason']);self.assertEqual([],result['claims'])

    def test_chained_follow_up_and_conditional_class_remain_bound_to_user_intent(self):
        _,intent=resolve('Aur uski deadline?',['Latest conditional Court directions?','Iski deadline kya hai?'],[])
        self.assertTrue(intent['latest']);self.assertEqual('Conditional',intent['directionClass'])

    def test_deterministic_whole_history_never_silently_truncates_eight_claims(self):
        data=artifact();data['orders']=[order([fact('The LAC shall file a report.')],orderDate=f'2026-01-{day:02d}') for day in range(1,13)]
        result=answer(data,'case-a','Show every order in the timeline',Never(),inference_busy=True)
        self.assertEqual(12,len(result['claims']))

    def test_standalone_general_follow_up_is_local_and_never_accepts_Court_facts(self):
        class Local:
            def extract(self,*args):return {'response':'Here is a short explanation.'}
        reply=general_answer('Please continue.',Local(),history=[{'question':'Explain this sentence','answer':'It describes a friendly greeting.'}],standalone=True)
        self.assertEqual('GeneralLocal',reply['mode']);self.assertEqual([],reply['claims'])
        with self.assertRaises(ValueError): general_answer('What did the Court order?',Local(),standalone=True)

if __name__=='__main__':unittest.main()
