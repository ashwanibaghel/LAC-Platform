"""Offline actual-output metrics; no inference, repairs or training-data edits."""
import argparse
import json
from pathlib import Path
import sys
from collections import defaultdict

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from foundation import write_json


def ratio(counter, key, numerator, denominator):
    old = counter.setdefault(key, [0, 0])
    old[0] += numerator
    old[1] += denominator


def analyze(rows, records):
    results = {}
    for mode in ('stock_4b', 'fine_tuned_4b'):
        counter = {'unexpected_office_action': 0, 'unexpected_compliance': 0,
                   'unexpected_next_date': 0, 'case_mixing': 0, 'unretrieved_ids': 0}
        for row in [r for r in rows if r['model'] == mode]:
            record = records[row['id']]
            prediction = row.get('parsed_output') or {}
            ratio(counter, 'exact_target', int(row['exact_target']), 1)
            ratio(counter, 'parser_acceptance', int(row['parser_accepted']), 1)
            if record['contract'] == 'anchors':
                wanted = {(r['anchorId'], r['category'], r['field'], r['scope']) for r in record['expected']['facts']}
                got = {(r['anchorId'], r['category'], r['field'], r['scope']) for r in prediction.get('facts', [])}
                ratio(counter, 'semantic_role_and_attribution', len(wanted & got), len(wanted))
                ratio(counter, 'court_direction_precision', len([r for r in wanted & got if r[1] == 'COURT_DIRECTION']), len([r for r in got if r[1] == 'COURT_DIRECTION']))
                ratio(counter, 'court_direction_recall', len([r for r in wanted & got if r[1] == 'COURT_DIRECTION']), len([r for r in wanted if r[1] == 'COURT_DIRECTION']))
                counter['wrong_role_or_scope'] = counter.get('wrong_role_or_scope', 0) + len(got - wanted)
                continue
            entries = record['input']['availableEvidence']
            wanted = {r['factId'] for r in record['expected']['claims']}
            got = {r['factId'] for r in prediction.get('claims', [])}
            counter['unretrieved_ids'] += len([i for i in got if not 0 <= i < len(entries)])
            task = record['task']
            if task == 'important_fact_selection':
                ratio(counter, 'important_fact_recall', len(got & wanted), len(wanted))
            if task == 'office_action_detection':
                ratio(counter, 'office_action_precision', len(got & wanted), len(got))
                ratio(counter, 'office_action_recall', len(got & wanted), len(wanted))
                counter['unexpected_office_action'] += len(got - wanted)
            if task == 'compliance_state':
                ratio(counter, 'compliance_state_correctness', int(got == wanted), 1)
                counter['unexpected_compliance'] += len(got - wanted)
            if task == 'date_specific_retrieval_or_QA':
                ratio(counter, 'date_specific_qa_exact', int(got == wanted), 1)
            if task == 'multi_order_current_position':
                ratio(counter, 'bounded_current_position_exact', int(got == wanted), 1)
            for category, key in [('PETITIONER_SUBMISSION', 'petitioner_position_retrieval'),
                                  ('LAC_OR_RESPONDENT_SUBMISSION', 'respondent_position_retrieval'),
                                  ('COURT_FINDING', 'court_finding_retrieval')]:
                positive = {i for i in wanted if entries[i]['category'] == category}
                ratio(counter, key, len(got & positive), len(positive))
            cited = [entries[i]['source'] for i in got if 0 <= i < len(entries)]
            ratio(counter, 'bounded_order_page_citations', len(cited), len(got))
            if row['id'].endswith('-next'):
                ratio(counter, 'next_hearing_exact', int(got == wanted), 1)
                counter['unexpected_next_date'] += len(got - wanted)
            if 'last-three' in row['id']:
                ratio(counter, 'supplied_chronology_exact', int(prediction == record['expected']), 1)
            if task == 'officer_qualitative_qa':
                ratio(counter, 'officer_question_exact', int(got == wanted), 1)
        results[mode] = counter
    old, new = results['stock_4b'], results['fine_tuned_4b']
    safe_keys = ('unexpected_office_action', 'unexpected_compliance', 'unexpected_next_date', 'unretrieved_ids', 'case_mixing', 'wrong_role_or_scope')
    regression = [key for key in safe_keys if new.get(key, 0) > old.get(key, 0)]
    total = old['exact_target'][1]
    improvement = new['exact_target'][0] - old['exact_target'][0]
    return {'metrics': results, 'safety_regressions': regression,
        'material_improvement_rule_frozen_before_output': 'At least 5 percentage points exact-target gain; no safety-count regression; manual source-chain acceptance still required',
        'numerical_candidate_gate': not regression and improvement / total >= .05,
        'exact_gain': [improvement, total],
        'limitations': ['Retrieval-role metrics use supplied source-audited structured roles, not extraction role recognition.',
                       'Citations are mechanically exact bounded IDs; this does not prove semantic answer relevance.',
                       'Case mixing is structurally prohibited; zero is not an adversarial cross-case eval.',
                       'Only supplied actual source chains, not complete court history.']}


if __name__ == '__main__':
    cli = argparse.ArgumentParser()
    cli.add_argument('--training-output', required=True, type=Path)
    cli.add_argument('--officer-output', required=True, type=Path)
    cli.add_argument('--bundle', required=True, type=Path)
    cli.add_argument('--output', required=True, type=Path)
    args = cli.parse_args()
    records = {}
    for split in ('validation', 'blind'):
        for line in (args.bundle / (split + '.jsonl')).read_text(encoding='utf-8').splitlines():
            record = json.loads(line)
            records[record['id']] = record
    for line in (HERE.parent / 'pilot-v1/officer-evaluation.jsonl').read_text(encoding='utf-8').splitlines():
        record = json.loads(line)
        records[record['id']] = record
    rows = json.loads((args.training_output / 'evaluation-outputs.json').read_text()) + json.loads((args.officer_output / 'evaluation-outputs.json').read_text())
    write_json(args.output, analyze(rows, records))
    print(json.dumps(analyze(rows, records), indent=2))
