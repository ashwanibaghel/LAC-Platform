import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parents[1]))
from benchmark.multiview_rapidocr_v10 import agreement,semantic
from benchmark.confidence_policy_v10 import field_confidence,freeze_threshold,row_safeexact

def p(value): return {'normalizedPrediction':value}
class MultiViewTests(unittest.TestCase):
 def test_all_view_agreement(self): self.assertEqual(agreement('khasra',[p('6//25')]*4)[:2],('StrongAgreement','6//25'))
 def test_majority_with_invalid_views_is_strong(self): self.assertEqual(agreement('area',[p('4-16'),p('4-16'),p('4-16'),p('bad')])[0],'StrongAgreement')
 def test_meaningful_disagreement_is_not_strong(self): self.assertEqual(agreement('area',[p('4-16'),p('4-16'),p('4-12'),p('bad')])[0],'ModerateAgreement')
 def test_empty_is_unreadable(self): self.assertEqual(agreement('khasra',[p(''),p('bad')])[0],'Unreadable')
 def test_area_semantics_not_identifier_repair(self): self.assertEqual(semantic('area','4 -- 16'),'4-16'); self.assertNotEqual(semantic('khasra','6//25'),semantic('khasra','6//26'))
 def test_master_validates_after_ocr_and_never_corrects_it(self):
  self.assertEqual(field_confidence('khasra','StrongAgreement','6//25',{'6//25'}),'HighConfidence')
  self.assertEqual(field_confidence('khasra','StrongAgreement','6//26',{'6//25'}),'NeedsReview')
 def test_area_never_uses_master_correction(self): self.assertEqual(field_confidence('area','StrongAgreement','4-16',{'9//9'}),'HighConfidence')
 def test_field_help_does_not_make_incomplete_row_safe(self):
  self.assertFalse(row_safeexact({'khasra':'HighConfidence','recorded':'NeedsReview','awarded':'NeedsReview'},True,True))
 def test_full_safeexact_requires_every_critical_field_and_geometry(self):
  fields={'khasra':'HighConfidence','recorded':'HighConfidence','awarded':'HighConfidence'}
  self.assertTrue(row_safeexact(fields,True,True)); self.assertFalse(row_safeexact(fields,False,True))
 def test_threshold_freezing_requires_both_evaluation_pools(self): self.assertFalse(freeze_threshold([{'coverage':8,'incorrect':0},{'coverage':8,'incorrect':1}]))
if __name__=='__main__': unittest.main()
