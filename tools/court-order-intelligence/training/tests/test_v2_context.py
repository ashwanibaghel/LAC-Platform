"""No-GPU tokenizer-boundary and exact-template tests."""
import sys
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'kaggle'))
from audit_v2_context import check, messages
from reload_smoke import inference_messages


class RecordingTokenizer:
    def __init__(self):
        self.calls = []

    def apply_chat_template(self, values, tokenize, add_generation_prompt):
        self.calls.append(values)
        # Model-independent deterministic tokens. Matching assistant prefix
        # tests the boundary logic, not a claim about Qwen token lengths.
        encoded = list(''.join(v['content'] for v in values[:2]) + '|assistant|')
        if len(values) == 3:
            encoded += list(values[2]['content'])
        return encoded


class ContextSafety(unittest.TestCase):
    def setUp(self):
        self.example = {'id': 'synthetic', 'task': 'order_digest',
                        'input': {'currentCase': 'W.P.(C) 123/2020', 'question': 'What happened?',
                                  'availableEvidence': [{'text': 'Source evidence unchanged.'}]},
                        'target': {'claims': []}}
        self.schemas = {'claims': {'type': 'object'}, 'anchors': {'type': 'object'}}

    def test_inference_template_exactly_matches_proven_kaggle_template(self):
        tokenizer = RecordingTokenizer()
        check(tokenizer, self.example, self.schemas)
        kind, values = messages(self.example)
        self.assertEqual(tokenizer.calls[-1], inference_messages({'messages': values, 'contract': kind}, self.schemas))

    def test_oversize_is_reported_not_truncated_or_repaired(self):
        tokenizer = RecordingTokenizer()
        self.example['input']['availableEvidence'][0]['text'] *= 100
        result = check(tokenizer, self.example, self.schemas, cap=20)
        self.assertFalse(result['training_fits'])
        self.assertFalse(result['inference_with_output_reserve_fits'])
        self.assertIn(self.example['input']['availableEvidence'][0]['text'], tokenizer.calls[0][1]['content'])

    def test_evaluation_does_not_receive_assistant_target(self):
        tokenizer = RecordingTokenizer()
        check(tokenizer, self.example, self.schemas)
        self.assertEqual([m['role'] for m in tokenizer.calls[-1]], ['system', 'user'])

    def test_output_allowance_counts_against_context(self):
        tokenizer = RecordingTokenizer()
        result = check(tokenizer, self.example, self.schemas, cap=100000, output_allowance=100000)
        self.assertTrue(result['training_fits'])
        self.assertFalse(result['inference_with_output_reserve_fits'])
        self.assertTrue(result['existing_pilot_inference_prompt_fits'])

    def test_actual_pilot_cap_checks_prompt_not_prompt_plus_reserve(self):
        source = (ROOT / 'kaggle/pilot_train.py').read_text()
        self.assertIn('if prompt.shape[1] > config["max_sequence_length"]:', source)
        self.assertIn('max_new_tokens=512', source)

    def test_broken_assistant_prefix_fails_closed(self):
        class Broken(RecordingTokenizer):
            def apply_chat_template(self, values, tokenize, add_generation_prompt):
                return [len(values)]
        with self.assertRaisesRegex(ValueError, 'Assistant boundary'):
            check(Broken(), self.example, self.schemas)


if __name__ == '__main__':
    unittest.main()
