# Local document-intelligence worker runtime

The Award worker is local-only. Its proven runtime is **Python 3.11** with
RapidOCR's Torch backend; it does not use `onnxruntime`.

## Proven dependency set

`requirements.txt` records the direct packages used by the current worker:

- `rapidocr==3.9.2`
- `torch==2.14.0` and `torchvision==0.29.0` (CPU is valid)
- `PyMuPDF==1.28.2` and `Pillow==12.3.0`
- `transformers==4.57.1` and `timm==1.0.29` for the existing Table Transformer
  geometry stage

RapidOCR is configured by `worker.py` with `EngineType.TORCH` for detection,
classification, and recognition. Its local detector, classifier, and recognizer
weights must be present before a production analysis is started.

## Restore or create the local runtime

Prefer the existing benchmark environment when it is available:

```powershell
C:\LAC-Platform\tools\document-intelligence-benchmark\.venv\Scripts\python.exe --version
```

If a dedicated worker environment must be rebuilt, use Python 3.11 and install
only the pinned worker dependencies:

```powershell
py -3.11 -m venv tools\document-intelligence-worker\.venv
tools\document-intelligence-worker\.venv\Scripts\python.exe -m pip install --upgrade pip
tools\document-intelligence-worker\.venv\Scripts\python.exe -m pip install -r tools\document-intelligence-worker\requirements.txt
```

Do not commit the virtual environment, model cache, PDFs, page renders, OCR
output, or temporary worker files.

## Application configuration

Configure paths locally through ASP.NET Core environment variables, not through
committed machine-specific paths:

```powershell
[Environment]::SetEnvironmentVariable('DocumentIntelligence__Enabled', 'true', 'User')
[Environment]::SetEnvironmentVariable('DocumentIntelligence__PythonExecutable', 'C:\path\to\python.exe', 'User')
[Environment]::SetEnvironmentVariable('DocumentIntelligence__WorkerScript', 'C:\path\to\worker.py', 'User')
[Environment]::SetEnvironmentVariable('DocumentIntelligence__WorkingDirectory', 'C:\path\to\document-intelligence-worker', 'User')
```

Restart the API after changing these variables. Worker invocation remains
explicit; enabling this local runtime does not start analysis or create records.
