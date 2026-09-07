import json, re
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).parents[1]))
from benchmark.score_ocr_v5 import structural

def test_structural_area_accepts_separator_noise():
    assert structural('recorded', '0.- 14') == structural('recorded', '0 -- 14')

def test_structural_khasra_keeps_qualifier():
    assert structural('khasra', '8//24/1 min') == '8//24/1min'
    assert structural('khasra', '8//24/2') != structural('khasra', '8//24/1 min')

def test_consensus_does_not_use_master_data():
    p=Path(__file__).parents[1]/'real-output'/'ocr-consensus-analysis.json'
    if p.exists():
        data=json.loads(p.read_text())
        assert 'voting' in data.get('method','').lower()
        assert 'canonical' not in json.dumps(data.get('cells',[])).lower()
