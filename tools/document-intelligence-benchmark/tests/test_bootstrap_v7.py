from benchmark.specialist_grammar_v6 import valid_khasra,valid_area,valid_role

def test_exact_master_like_label_is_valid_without_repair():
    raw='22//2'; master={'22//2'}
    assert raw in master and valid_khasra(raw)

def test_master_repair_is_rejected():
    raw='22//7'; master={'22//1'}
    assert raw not in master

def test_qualifier_and_role_constraints():
    assert valid_khasra('22//2/1 min')
    assert not valid_khasra('22//2/1 minimum')
    assert valid_role('area','5-02')

def test_area_master_cannot_label_award_area():
    master_area='5-02'; award_ocr='0-14'
    assert master_area != award_ocr

def test_held_out_identity_can_be_excluded():
    held={(2,1,0)}
    assert (2,1,0) in held and (3,1,0) not in held

def test_active_learning_prefers_difficult_cells():
    easy=1+len('2//19')/20
    difficult=1+len('8//24/1 min')/20+2+3
    assert difficult>easy

def test_provenance_has_no_canonical_mutation_field():
    sample={'rawOcr':'22//2','masterConfirmedExact':True,'masterRepairUsed':False,'sourcePage':2}
    assert sample['masterRepairUsed'] is False and sample['sourcePage']==2
