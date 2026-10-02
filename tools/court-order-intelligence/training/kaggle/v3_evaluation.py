"""One V3 contract for all four comparisons; legacy continuity is separate.

This module never repairs a model answer or looks at gold before parsing.
The caller supplies the SAME frozen record/schema and decoding configuration
for stock, V1, V2 and V3. Frozen legacy evaluation artifacts are not rewritten.
"""
import json
import jsonschema

from semantic_gate import VERSION
from v3_contract import parse_v3_output
from pilot_v2_train import detailed_score

MODES = ('stock_4b', 'pilot_v1', 'pilot_v2', 'pilot_v3')


def assess_output(mode, raw, record, schemas, runtime_dir):
    if mode not in MODES:
        raise ValueError('Unknown V3 comparison mode')
    accepted, payload, error = False, None, None
    try:
        payload = parse_v3_output(raw, record, schemas, runtime_dir)
        accepted = True
    except (ValueError, KeyError, TypeError, jsonschema.ValidationError) as failure:
        error = str(failure)
        try:
            payload = json.loads(raw)
        except ValueError:
            pass
    # Gold is first accessed by scoring, AFTER the identical independent gate.
    return dict(model=mode, semantic_contract=VERSION, raw_output=raw,
                parsed_output=payload, parser_error=error,
                **detailed_score(record, payload, accepted))
