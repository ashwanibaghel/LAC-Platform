import json
import pytest
from benchmark.training_guard_v8 import assert_no_gold_leak

def gold(tmp_path):
 p=tmp_path/'gold.json'; p.write_text(json.dumps([{'row':1,'column':0}])); return p

def test_training_guard_rejects_gold_coordinate(tmp_path):
 with pytest.raises(ValueError): assert_no_gold_leak([{'sourcePage':2,'sourceRow':1,'sourceColumn':0,'image':'x'}],gold(tmp_path))

def test_training_guard_allows_different_source_row(tmp_path):
 assert_no_gold_leak([{'sourcePage':2,'sourceRow':2,'sourceColumn':0,'image':'x'}],gold(tmp_path))

def test_training_guard_ignores_fictional_synthetic_item(tmp_path):
 assert_no_gold_leak([{'image':'synthetic.png','text':'22//2'}],gold(tmp_path))
