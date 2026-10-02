"""Allowlisted private inference-only upload. No training examples altered."""
import argparse
import hashlib
from pathlib import Path
import shutil
import sys
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from foundation import write_json


if __name__ == '__main__':
    cli = argparse.ArgumentParser()
    cli.add_argument('--output', required=True, type=Path)
    output = cli.parse_args().output.resolve()
    output.mkdir()  # Fresh destination only.
    source = HERE.parent / 'pilot-v1/officer-evaluation.jsonl'
    shutil.copyfile(source, output / source.name)
    write_json(output / 'officer-manifest.json', {'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
        'frozen_before_model_output_inspection': True, 'freeze_commit': '6e5a18d0dcd3f60485f7b1cc2c7cddbec62db1bc',
        'training_eligible': False, 'private_workbook_included': False, 'count': 26})
    write_json(output / 'dataset-metadata.json', {'title': 'LAC Pilot V1 Frozen Officer Evaluation',
        'id': 'ashwanibaghel9027/lac-pilot-v1-officer-evaluation', 'licenses': [{'name': 'other'}]})
