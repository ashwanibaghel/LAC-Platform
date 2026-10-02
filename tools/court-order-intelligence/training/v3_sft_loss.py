"""Exact masked causal SFT loss, retaining every native-context input token.

Pinned Qwen3 supports tensor logits_to_keep *after* its complete decoder
forward. Only positions whose next-token labels are supervised need LM-head
logits. This changes neither attention/evidence nor the loss/gradient target.
No model surgery, quantization change, truncation, gold retrieval or inference.
"""
import torch
from torch.nn import functional as F


def masked_causal_loss(model, inputs, num_items_in_batch=None):
    if getattr(model.config, 'model_type', None) != 'qwen3':
        raise ValueError('Pinned Qwen3 logits contract required')
    labels = inputs['labels']
    if labels.ndim != 2 or labels.shape[0] != 1 or inputs['input_ids'].shape != labels.shape:
        raise ValueError('Tested single-record full-context loss required')
    positions = torch.nonzero(labels[0, 1:] != -100, as_tuple=False).flatten()
    if positions.numel() == 0:
        raise ValueError('No supervised next-token targets')
    values = {k: v for k, v in inputs.items() if k != 'labels'}
    outputs = model(**values, logits_to_keep=positions)
    if outputs.logits.shape[:2] != (1, positions.numel()):
        raise ValueError('Model did not honor exact supervised-logits positions')
    targets = labels[0, positions + 1]
    # Same float32 CE and accumulation denominator as pinned HF causal loss.
    reduction = 'mean' if num_items_in_batch is None else 'sum'
    loss = F.cross_entropy(outputs.logits[0].float(), targets, reduction=reduction)
    if num_items_in_batch is not None:
        if float(num_items_in_batch) <= 0:
            raise ValueError('Positive accumulation target count required')
        loss = loss / num_items_in_batch
    return loss, outputs


def verify_exact_loss():
    """Run on the actual pinned kernel stack before consuming model/GPU memory."""
    from copy import deepcopy
    from transformers.models.qwen3.configuration_qwen3 import Qwen3Config
    from transformers.models.qwen3.modeling_qwen3 import Qwen3ForCausalLM
    torch.manual_seed(31)
    template = Qwen3ForCausalLM(Qwen3Config(vocab_size=37, hidden_size=16,
        intermediate_size=32, num_hidden_layers=1, num_attention_heads=2,
        num_key_value_heads=1, head_dim=8, attention_dropout=0, use_cache=False,
        tie_word_embeddings=False))
    max_error = 0.0
    for mask, denominator in (([-100] * 9 + [4, 3, 2], None),
                              ([-100, 4, -100, 7, -100, 6], None),
                              ([-100] * 9 + [4, 3, 2], 11)):
        ordinary, efficient = deepcopy(template), deepcopy(template)
        batch = dict(input_ids=torch.arange(len(mask)).unsqueeze(0) % 37,
                     labels=torch.tensor([mask]), attention_mask=torch.ones(1, len(mask), dtype=torch.long))
        expected = ordinary(**batch, **({} if denominator is None else {'num_items_in_batch': denominator})).loss
        actual, _ = masked_causal_loss(efficient, batch, denominator)
        torch.testing.assert_close(actual, expected, atol=1e-6, rtol=1e-6)
        max_error = max(max_error, float((actual - expected).abs().detach()))
        expected.backward()
        actual.backward()
        for p, q in zip(ordinary.parameters(), efficient.parameters()):
            torch.testing.assert_close(p.grad, q.grad, atol=1e-6, rtol=1e-5)
    return dict(result='PASS', fixtures=3, loss_and_all_gradients_equivalent=True,
                full_input_retained=True, max_loss_error=max_error)
