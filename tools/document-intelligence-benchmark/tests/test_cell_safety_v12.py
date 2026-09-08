import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parents[1]))
from benchmark.cell_safety_v12 import contamination_status,normalize_area_evidence,token_belongs_to_cell
from benchmark.specialist_grammar_v6 import normalize_area
class CellSafetyTests(unittest.TestCase):
 def test_separator_forms_normalize_without_digit_edit(self):
  for raw in ('22 -- 3','22 - - 3',' 22  -  3 ','22--3'): self.assertEqual(normalize_area_evidence(raw)['normalizedValue'],'22-3')
 def test_raw_text_and_reason_are_preserved(self):
  x=normalize_area_evidence('22 -- 3'); self.assertEqual(x['rawOcrText'],'22 -- 3'); self.assertEqual(x['normalizationReason'],'AreaSeparatorNormalization')
 def test_ambiguous_area_is_review(self): self.assertEqual(normalize_area_evidence('22 -- ?3')['status'],'NeedsReview')
 def test_khasra_is_not_normalized(self): self.assertEqual(' '.join('22//3/1'.split()),'22//3/1')
 def test_page_number_outside_cell_is_rejected(self): self.assertFalse(token_belongs_to_cell({'x':105,'y':5,'width':5,'height':5},{'x':0,'y':0,'width':100,'height':30}))
 def test_neighbour_cell_token_is_rejected(self): self.assertFalse(token_belongs_to_cell({'x':101,'y':5,'width':8,'height':8},{'x':0,'y':0,'width':100,'height':30}))
 def test_inner_recovery_preserves_outer_evidence(self): self.assertEqual(contamination_status('22-3 7','22-3','area')[0],'ContaminationRecovered')
 def test_existing_normalizer_accepts_spaced_dash(self): self.assertEqual(normalize_area('22 - - 3'),'22-3')
