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
