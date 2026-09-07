from benchmark.specialist_grammar_v6 import valid_area,valid_khasra,valid_role

def test_synthetic_khasra_shape():
    assert valid_khasra('22//1/6') and valid_khasra('22//2/1 min')

def test_valid_area_and_biswa_range():
    assert valid_area('149-15') and valid_area('0-01')
    assert not valid_area('1-20')

def test_role_grammar_preserves_slashes_and_qualifier():
    assert valid_role('khasra','22//2/1 min')
    assert not valid_role('khasra','22/1')
    assert valid_role('qualifier','min')

def test_grammar_rejects_impossible_output_without_repair():
    assert not valid_role('area','22-99')
    assert not valid_role('killa','22//1')

def test_safe_exact_requires_exact_recognition():
    assert '22//1' != '22//2'
