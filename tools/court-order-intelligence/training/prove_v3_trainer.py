"""Offline actual CPU Trainer proof. No base weights, gold, model hub or GPU.

Tiny randomly initialized arithmetic network, SOFTWARE fixtures only. Skip-update
simulation verifies consumed-sample accounting, not CUDA GradScaler numerical
behavior. Actual FP16 applied/skipped updates must still be measured on Kaggle.
"""
import argparse
import json
from pathlib import Path
import tempfile

import torch
import transformers
import accelerate
from transformers import TrainingArguments, TrainerCallback
from v3_trainer import CurriculumDataset, CurriculumTrainer
from v3_sampler import WEIGHTS


def fixtures():
    return [{'id': f'{task}-{matter}-{polarity}', 'task': task,
             'matter_id': f'fixture-{matter}',
             'leakage_group': 'fixture-connected' if matter < 2 else f'fixture-{matter}',
             'target': {'claims': [{'factId': 0}] if polarity else []}}
            for task in WEIGHTS for matter in range(4) for polarity in (0, 1)]


class Tiny(torch.nn.Module):
    def __init__(self):
        super().__init__()
        self.layer = torch.nn.Linear(1, 1)

    def forward(self, input_ids, labels):
        return {'loss': ((self.layer(input_ids.float()) - labels.float()) ** 2).mean()}


class SkipUpdate(torch.optim.SGD):
    def __init__(self, params):
        super().__init__(params, lr=0.001)
        self.attempts = 0
        self.applied = 0

    def step(self, closure=None):
        self.attempts += 1
        if self.attempts == 3:
            return None  # Simulate scaler skip without skipping sample consumption.
        self.applied += 1
        return super().step(closure)

    def state_dict(self):
        result = super().state_dict()
        result['proof_update_accounting'] = {'attempts': self.attempts, 'applied': self.applied}
        return result

    def load_state_dict(self, state):
        state = dict(state)
        accounting = state.pop('proof_update_accounting')
        super().load_state_dict(state)
        self.attempts, self.applied = accounting['attempts'], accounting['applied']


class Pause(TrainerCallback):
    def on_step_end(self, args, state, control, **kwargs):
        if state.global_step == 10:
            control.should_save = True
            control.should_training_stop = True


def run(output):
    if transformers.__version__ != '4.56.2' or accelerate.__version__ != '1.10.1':
        raise ValueError('Use pinned production Trainer/Accelerate versions')
    if output.exists():
        raise ValueError('Fresh proof report path required')
    torch.set_num_threads(1)
    rows = fixtures()
    original = json.dumps(rows, sort_keys=True)
    encoded = {r['id']: {'input_ids': [float(i % 5)], 'labels': [0.0]} for i, r in enumerate(rows)}
    data = CurriculumDataset(rows, encoded, 20261003, 80, 4)
    def collate(samples):
        return {key: torch.tensor([s[key] for s in samples]) for key in samples[0]}
    def trainer(path):
        model = Tiny()
        optimizer = SkipUpdate(model.parameters())
        args = TrainingArguments(output_dir=str(path), max_steps=80, use_cpu=True,
            per_device_train_batch_size=1, gradient_accumulation_steps=4,
            dataloader_num_workers=0, remove_unused_columns=False, ignore_data_skip=False,
            save_strategy='no', logging_strategy='no', report_to='none', disable_tqdm=True,
            seed=20261003, data_seed=20261003, save_safetensors=False)
        return CurriculumTrainer(model=model, args=args, train_dataset=data,
                                 data_collator=collate, optimizers=(optimizer, None))
    with tempfile.TemporaryDirectory(prefix='v3-trainer-proof-') as directory:
        root = Path(directory)
        continuous = trainer(root / 'continuous')
        continuous.train()
        first = trainer(root / 'resume')
        first.add_callback(Pause())
        first.train()
        checkpoint = root / 'resume/checkpoint-10'
        resumed = trainer(root / 'resume')
        resumed.train(resume_from_checkpoint=str(checkpoint))
        joined = first.consumed_ids + resumed.consumed_ids
        if joined != continuous.consumed_ids or joined != list(data.ids):
            raise ValueError('Actual Trainer consumed stream diverged after resume')
        if first.state.global_step != 10 or resumed.state.global_step != 80:
            raise ValueError('Actual Trainer step accounting failed')
        if continuous.optimizer.optimizer.attempts != 80 or continuous.optimizer.optimizer.applied != 79:
            raise ValueError('Skipped-update simulation invalid')
        if resumed.optimizer.optimizer.attempts != 80 or resumed.optimizer.optimizer.applied != 79:
            raise ValueError('Skipped-update accounting was lost on optimizer resume')
        if json.dumps(rows, sort_keys=True) != original:
            raise ValueError('Fixtures mutated')
        report = {'result': 'PASS', 'software_fixture_only': True, 'training_gold_used': False,
            'gpu_used': False, 'network_used': False, 'torch': torch.__version__,
            'transformers': transformers.__version__, 'accelerate': accelerate.__version__,
            'continuous_steps': 80, 'checkpoint_step': 10, 'resumed_final_step': 80,
            'consumed_microbatches': len(joined), 'continuous_resume_ids_exact_match': True,
            'simulated_applied_updates': 79, 'simulated_skips': 1,
            'real_cuda_gradscaler_proven': False, 'exposure': data.report,
            'curriculum_state': data.state_at(80), 'consumed_ids': joined}
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, sort_keys=True, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({k: v for k, v in report.items() if k not in ('exposure', 'curriculum_state', 'consumed_ids')}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    run(parser.parse_args().output)
