"""Finite, immutable Curriculum-selected map dataset for pinned HF Trainer.

Single-device/batch-one only. Sequential Trainer access, complete accumulation
boundaries, unchanged dataset on resume: Trainer alone skips consumed samples.
No moving generator cursor/prefetch state and no double skipping.
"""
import json
from pathlib import Path

from torch.utils.data import Dataset, SequentialSampler
from transformers import Trainer, TrainerCallback

from v3_sampler import Curriculum, consumed_checkpoint, exposure_report


class CurriculumDataset(Dataset):
    def __init__(self, rows, encoded_by_id, seed, steps, accumulation, weights=None):
        if isinstance(steps, bool) or not isinstance(steps, int) or not 1 <= steps <= 240:
            raise ValueError('Bounded Trainer steps required')
        if isinstance(accumulation, bool) or accumulation not in range(1, 9):
            raise ValueError('Bounded fixed accumulation required')
        if set(encoded_by_id) != {row['id'] for row in rows}:
            raise ValueError('All original gold must be encoded, no missing/truncated records')
        self.rows, self.seed, self.accumulation = rows, seed, accumulation
        self.encoded = encoded_by_id
        self.steps = steps
        self.curriculum = Curriculum(rows, seed, weights)
        self.ids = tuple(self.curriculum.next_id() for _ in range(steps * accumulation))
        self.report = exposure_report(rows, seed, len(self.ids), weights)

    def __len__(self):
        return len(self.ids)

    def __getitem__(self, index):
        return dict(self.encoded[self.ids[index]], curriculum_index=index)

    def state_at(self, step):
        if not 0 <= step <= self.steps:
            raise ValueError('Checkpoint step outside curriculum')
        return consumed_checkpoint(self.rows, self.seed, step * self.accumulation,
                                   self.curriculum.weights)


class CurriculumCheckpoint(TrainerCallback):
    def __init__(self, dataset):
        self.dataset = dataset

    def on_save(self, args, state, control, **kwargs):
        # HF completed this global step even when the scaler skipped its update.
        target = Path(args.output_dir) / f'checkpoint-{state.global_step}' / 'curriculum-state.json'
        target.write_text(json.dumps(self.dataset.state_at(state.global_step), sort_keys=True, indent=2) + '\n',
                          encoding='utf-8')


class CurriculumTrainer(Trainer):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        data = self.train_dataset
        if not isinstance(data, CurriculumDataset):
            raise ValueError('Actual CurriculumDataset required')
        if (self.args.world_size != 1 or self.args.per_device_train_batch_size != 1
                or self.args.gradient_accumulation_steps != data.accumulation
                or self.args.max_steps != data.steps or self.args.ignore_data_skip
                or self.args.remove_unused_columns or self.args.dataloader_num_workers != 0):
            raise ValueError('Trainer settings violate tested single-stream resume contract')
        self.consumed_ids = []
        self.add_callback(CurriculumCheckpoint(data))

    def _get_train_sampler(self, train_dataset=None):
        return SequentialSampler(train_dataset if train_dataset is not None else self.train_dataset)

    def training_step(self, model, inputs, num_items_in_batch=None):
        index = inputs.pop('curriculum_index')
        if index.numel() != 1:
            raise ValueError('One logical sample per microbatch required')
        value = int(index.item())
        self.consumed_ids.append(self.train_dataset.ids[value])
        return super().training_step(model, inputs, num_items_in_batch)

    def train(self, resume_from_checkpoint=None, *args, **kwargs):
        if resume_from_checkpoint:
            checkpoint = Path(resume_from_checkpoint)
            step = json.loads((checkpoint / 'trainer_state.json').read_text())['global_step']
            actual = json.loads((checkpoint / 'curriculum-state.json').read_text())
            if actual != self.train_dataset.state_at(step):
                raise ValueError('Checkpoint consumed curriculum differs; refuse resume')
        return super().train(resume_from_checkpoint=resume_from_checkpoint, *args, **kwargs)
