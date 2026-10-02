"""Opt-in V3 parser: legacy structure/source checks followed by common task gate."""
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'kaggle'))
sys.path.insert(0, str(HERE.parent))
from smoke_contract import parse_runtime_output
from semantic_gate import validate_claims, chronology_context, VERSION


def parse_v3_output(text, record, schemas, runtime_dir):
    payload = parse_runtime_output(text, record, schemas, runtime_dir)
    if record['contract'] == 'claims':
        validate_claims(payload, record['input']['availableEvidence'], record['task'],
                        intent=record['input'].get('taskIntent'))
    return payload


def context_for_v3(input_value):
    """No target access; enrichment copies input and preserves exact source text."""
    from copy import deepcopy
    result = deepcopy(input_value)
    result['semanticGateVersion'] = VERSION
    if 'availableEvidence' in result:
        result['chronologyState'] = chronology_context(result['availableEvidence'])
    return result
