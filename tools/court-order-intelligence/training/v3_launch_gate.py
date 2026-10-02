"""Fail-closed final-mission launch checklist. No GPU/network at import.

Numeric counts must be produced by independent frozen-gold/source/leakage audit;
this pure checker cannot establish their truth. Signed-off evidence artifacts and
their hashes must be bound by the future allowlisted Kaggle bundle verifier.
"""
MINIMUMS = {
    'attribution_examples': 30, 'attribution_groups': 10,
    'multi_order_examples': 25, 'multi_order_groups': 8,
    'positive_lac_action_examples': 25,
    'validation_groups': 5, 'blind_groups': 5,
    'blind_positive_lac_actions': 1, 'blind_attribution_examples': 1,
    'blind_multi_order_examples': 1, 'blind_positive_compliance_examples': 1,
    'blind_positive_date_qa_examples': 1,
}
REQUIRED_PROOFS = (
    'positive_empty_exposure_audit', 'connected_groups_audited',
    'validation_frozen', 'blind_frozen', 'trainer_consumes_curriculum',
    'actual_trainer_resume_proven', 'skip_prefetch_accounting_proven',
    'semantic_failure_suite_passed', 'production_context_compatible',
    'source_actor_lifecycle_bound', 'token_context_audited',
    'no_dropped_or_truncated_examples', 'private_workbook_absent',
    'pdfs_absent', 'protected_training_overlap_absent',
    'final_budget_calculated', 'exposure_counts_from_final_dataset',
)


def assess(report):
    blockers = []
    for key, minimum in MINIMUMS.items():
        value = report.get(key)
        if isinstance(value, bool) or not isinstance(value, int) or value < minimum:
            blockers.append({'gate': key, 'actual': value, 'required_minimum': minimum})
    for key in REQUIRED_PROOFS:
        if report.get(key) is not True:
            blockers.append({'gate': key, 'actual': report.get(key), 'required': True})
    return {'decision': 'NO-GO' if blockers else 'GO', 'blockers': blockers}


def require_go(report):
    result = assess(report)
    if result['decision'] != 'GO':
        raise ValueError('V3 GPU launch forbidden: ' + ', '.join(b['gate'] for b in result['blockers']))
    return result
