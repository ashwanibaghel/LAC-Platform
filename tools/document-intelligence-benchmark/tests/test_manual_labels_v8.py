from benchmark.specialist_grammar_v6 import normalize_area,valid_area

def test_printed_double_dash_becomes_semantic_single_dash():
    assert normalize_area('4 -- 16') == '4-16'
    assert valid_area('4 -- 16')

def test_normalization_does_not_change_digits():
    assert normalize_area('149 -- 15') == '149-15'
