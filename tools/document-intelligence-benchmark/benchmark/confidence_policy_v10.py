"""Pure experimental confidence gates; no canonical data or master correction."""
from .specialist_grammar_v6 import valid_area, valid_khasra

def field_confidence(role, agreement_level, value, master_values=None):
    if agreement_level != 'StrongAgreement' or not value: return 'NeedsReview'
    if role == 'khasra':
        return 'HighConfidence' if valid_khasra(value) and value in (master_values or set()) else 'NeedsReview'
    if role == 'area': return 'HighConfidence' if valid_area(value) else 'NeedsReview'
    return 'NeedsReview'

def row_safeexact(fields, geometry_proven, explicit_rectangle, contradiction=False):
    """A review-assistance gate, never an automatic commit decision."""
    return bool(geometry_proven and explicit_rectangle and not contradiction and all(fields.get(key)=='HighConfidence' for key in ('khasra','recorded','awarded')))

def freeze_threshold(pool_reports):
    """A gate is selectable only when every independent labelled pool has zero errors."""
    return all(report['incorrect']==0 and report['coverage']>0 for report in pool_reports)
