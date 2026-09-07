# Local document-intelligence benchmark

This directory is an isolated, read-only experiment for evaluating local document
intelligence engines against Award PDFs. It is not part of the production
extraction pipeline and it never writes canonical LAC data.

## Privacy boundary

All inference is local. The runner accepts a local file path and writes page
renders, engine output, and reports only below an ignored local output directory.
Do not add real PDFs, page images, OCR text, names, or benchmark JSON to Git.

## Setup

```powershell
cd tools/document-intelligence-benchmark
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -r requirements-paddle.txt
python -m unittest discover -s tests
```

The first Paddle run downloads open-source model weights to `.cache`; this is a
software/model download only. The Award PDF is never uploaded or sent to an API.

## Run

```powershell
python -m benchmark.run_paddle --pdf "C:\path\to\local-award.pdf" --pages 1,2,7 --output real-output\paddle-sample
```

The full-document command is the same without `--pages`. The output directory is
ignored by Git. A later adapter may consume the normalized JSON without importing
Paddle types into the LAC domain.

`requirements-docling.txt` is intentionally a separate optional installation. It
is evaluated only if the Paddle sample cannot produce usable local structure.

## Scope

`benchmark.normalized` is the neutral page model. `benchmark.interpret` is a
deterministic experimental LAC interpretation pass. It uses no numeric correction:
uncertain identifiers remain uncertain and all candidates keep page/cell evidence.

## Phase 9 unified recognition crops

`benchmark.cell_crop_pipeline_v9` is the sole image-preparation boundary for
cell recognition experiments. It saves an untouched evidence crop plus a
deterministically normalized recognition crop and metadata. It does not edit
digits, slashes, dashes, or use master data.

```powershell
python -m benchmark.unify_cell_crops_v9 --root . --gold real-output\ocr-gold-manifest.json --human real-output\assisted-label-v10\manual-training.json --pseudo real-output\pseudo-v7\pseudo-verified.json --output real-output\unified-crops-v9
python -m benchmark.recognizer_shootout_v9 --manifests real-output\unified-crops-v9\unified-manifests.json --engine rapidocr --output real-output\unified-crops-v9\rapidocr.json
python -m benchmark.recognizer_shootout_v9 --manifests real-output\unified-crops-v9\unified-manifests.json --engine doctr --output real-output\unified-crops-v9\doctr.json
```

All these outputs remain local and ignored. The shootout records raw exact and
semantic exact separately; only area dash/whitespace formatting is semantic.
