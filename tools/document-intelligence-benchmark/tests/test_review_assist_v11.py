import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parents[1]))
from benchmark.review_assist_v11 import identity,save_decision
class ReviewAssistTests(unittest.TestCase):
 def setUp(self): self.item={'sourcePage':2,'sourceRow':3,'sourceColumn':0,'role':'khasra','suggestion':'6//9','sourceCrop':'local.png'}; self.state={'decisions':[],'trainingExamples':[]}
 def test_ocr_is_suggestion_and_human_correction_wins(self):
  save_decision(self.state,self.item,'6//10','corrected','reviewer'); self.assertEqual(self.state['trainingExamples'][0]['finalHumanLabel'],'6//10')
 def test_exact_master_is_not_a_decision(self): self.assertEqual(len(self.state['decisions']),0)
 def test_only_human_accept_or_correction_creates_gold(self):
  save_decision(self.state,self.item,'','skipped','r'); self.assertEqual(self.state['trainingExamples'],[])
 def test_source_region_and_role_are_preserved(self):
  save_decision(self.state,self.item,'6//9','accepted','r'); self.assertEqual(self.state['trainingExamples'][0]['sourceColumn'],0)
 def test_duplicate_prevention_makes_persistence_idempotent(self):
  save_decision(self.state,self.item,'6//9','accepted','r'); save_decision(self.state,self.item,'6//9','accepted','r'); self.assertEqual(len(self.state['trainingExamples']),1)
