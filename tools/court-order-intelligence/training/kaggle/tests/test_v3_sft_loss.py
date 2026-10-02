from copy import deepcopy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))


class ExactSftLossTests(unittest.TestCase):
    def test_full_context_loss_and_all_gradients_match_pinned_qwen3(self):
        import torch
        from transformers.models.qwen3.configuration_qwen3 import Qwen3Config
        from transformers.models.qwen3.modeling_qwen3 import Qwen3ForCausalLM
        from v3_sft_loss import masked_causal_loss
        torch.manual_seed(31)
        cfg = Qwen3Config(vocab_size=37, hidden_size=16, intermediate_size=32,
                          num_hidden_layers=1, num_attention_heads=2,
                          num_key_value_heads=1, head_dim=8, attention_dropout=0,
                          use_cache=False, tie_word_embeddings=False)
        template = Qwen3ForCausalLM(cfg)
        for mask, denominator in (([-100] * 9 + [4, 3, 2], None),
                                  ([-100, 4, -100, 7, -100, 6], None),
                                  ([-100] * 9 + [4, 3, 2], 11)):
            with self.subTest(mask=mask, denominator=denominator):
                ordinary, efficient = deepcopy(template), deepcopy(template)
                batch = dict(input_ids=torch.arange(len(mask)).unsqueeze(0) % 37,
                             labels=torch.tensor([mask]), attention_mask=torch.ones(1, len(mask), dtype=torch.long))
                before = {k: v.clone() for k, v in batch.items()}
                expected = ordinary(**batch, **({} if denominator is None else {'num_items_in_batch': denominator})).loss
                actual, outputs = masked_causal_loss(efficient, batch, denominator)
                torch.testing.assert_close(actual, expected, atol=1e-6, rtol=1e-6)
                expected.backward()
                actual.backward()
                for (name, p), (other, q) in zip(ordinary.named_parameters(), efficient.named_parameters()):
                    self.assertEqual(name, other)
                    torch.testing.assert_close(p.grad, q.grad, atol=1e-6, rtol=1e-5)
                for k in batch:
                    self.assertTrue(torch.equal(batch[k], before[k]))
                self.assertEqual(outputs.logits.shape[1], sum(x != -100 for x in mask[1:]))

    def test_no_architecture_or_evidence_changes_in_optimization(self):
        source = Path(__file__).resolve().parents[2] / 'v3_sft_loss.py'
        text = source.read_text()
        text = text.split('\ndef verify_exact_loss')[0]
        self.assertIn('outputs = model(**values, logits_to_keep=positions)', text)
        for forbidden in ('expected', 'truncate', 'input_ids[:,', 'lm_head =', 'gold['):
            self.assertNotIn(forbidden, text)


if __name__ == '__main__':
    unittest.main()
