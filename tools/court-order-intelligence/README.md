# Local Court Order Intelligence · Phase 1

This is source-backed, reviewable officer support, not a legal chatbot. Inference
is **literal 127.0.0.1 only**. No Court text goes to Hugging Face/GitHub/cloud AI.
Those sites are used only to obtain the runtime/model. No OCR, database schema,
canonical status/NDOH/proceeding mutation or live assisted-CAPTCHA action exists
in this pipeline.

## Prerequisites and exact local setup

Windows x64, Python 3.11+, llama.cpp Windows CPU server, local
`Qwen3-1.7B-Q4_K_M.gguf`. Keep runtime/model/cache/logs outside the repository.
The tested runtime is llama.cpp b11321; the model is the Unsloth quantization of
Qwen3-1.7B. Use the publisher file SHA to verify downloads. Do not substitute a
cloud endpoint if the local service is unavailable.

Example PowerShell from the feature checkout:

```powershell
python -m venv C:\LAC-CourtAI\venv
C:\LAC-CourtAI\venv\Scripts\python.exe -m pip install -r tools/court-order-intelligence/requirements.txt

# Download/extract the Windows CPU runtime and model to C:\LAC-CourtAI first:
# https://github.com/ggml-org/llama.cpp/releases/download/b11321/llama-b11321-bin-win-cpu-x64.zip
# https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf

.\scripts\start-court-local-model.ps1 -ServerExe C:\LAC-CourtAI\llama-server.exe -ModelPath C:\LAC-CourtAI\Qwen3-1.7B-Q4_K_M.gguf -RuntimeDirectory C:\LAC-CourtAI\runtime
```

Defaults: CPU-only, two threads, one inference slot, 4096 context, lower process
priority. Run one worker. Do not run heavyweight regression builds alongside
inference on an 8 GB machine; paging can make inference much slower. First
extraction may take several minutes per source. Text, output, page count and
per-request timeout are bounded; oversized/ambiguous sources fail closed.

## Registered Court Matter integration

Use the API's **existing resolved absolute** `Storage:ExtractionRoot`, not a
new worktree-relative default. Create a local jobs file outside Git:

```json
[{"caseId":"REGISTERED-COURT-CASE-GUID","caseNumber":"W.P.(C) 123/2025",
  "orders":[{"orderDate":"2025-01-01","officialUrl":"https://delhihighcourt.nic.in/app/showFileJudgment/KNOWN-OFFICIAL.pdf"}]}]
```

Use exact, already-known, query-free direct official PDF URLs. Date and exact
case identity must be confirmed in the downloaded source, not inferred from its
filename. Do not attach another case's PDF to a registered case GUID. The worker
does not discover orders via searches or submit any verification code.

```powershell
.\scripts\start-court-intelligence.ps1 -Mode Worker -PythonExe C:\LAC-CourtAI\venv\Scripts\python.exe -ExtractionRoot D:\LAC-Data\extraction -RuntimeDirectory C:\LAC-CourtAI\runtime -ModelVersion Qwen3-1.7B-Q4_K_M-b11321 -Jobs C:\LAC-CourtAI\case-jobs.local.json
.\scripts\start-court-intelligence.ps1 -Mode Questions -PythonExe C:\LAC-CourtAI\venv\Scripts\python.exe -ExtractionRoot D:\LAC-Data\extraction -RuntimeDirectory C:\LAC-CourtAI\runtime -ModelVersion Qwen3-1.7B-Q4_K_M-b11321
```

The worker writes `court-intelligence/v1/{caseId}/orders/*.json` and atomic
`current.json` beneath that extraction root. No PDFs retained. Run failures and
coverage gaps remain visible, never fabricated facts. Interrupted processing
retains `processingComplete=false`. Repeat jobs explicitly if a failed source
needs retry; Phase 1 does not automatically refresh Court PDFs in the background.

Authenticated API:

- `GET /api/court-cases/{id}/intelligence`: read-only artifact, existing Court
  authorization; 204 if not processed. No model dependency.
- `POST /api/court-cases/{id}/intelligence/ask`, `{"question":"..."}`: explicit
  officer action, same authorization, local Q&A only. No database mutation.

The API does not start the worker/model/Q&A service and remains usable when all
are off. Question service off => calm 503 message, existing intelligence still
readable. No question/answer conversation is persisted in Phase 1.

## Grounded “Ask this case”

Natural question → deterministic field/year/latest-order retrieval within that
single case artifact → bounded evidence → local model selects/composes concise
extractive claims → independent exact-text/citation validation → attributed
answer/evidence. Every answer citation is built from its retrieved order/page,
not a model-provided URL/page. Negations/conditions cannot be removed. Party
submissions are labeled as submissions, never established Court facts. Model
memory, other cases, internet search and unsupported facts are prohibited.
Insufficient evidence returns exactly:

“Available Court orders do not establish this fact.”

Extractive answers intentionally trade fluency for auditability. Broad semantic
questions not supported by current field retrieval may return insufficient
evidence. Context is bounded; this is not complete legal coverage or a substitute
for source review. Compliance linkage is deliberately conservative: absent an
explicit later action/date linkage, completion remains unconfirmed.

The small model returns source-anchor IDs and classifications during extraction,
and retrieved fact IDs during answering. The runtime expands those IDs to exact
native source passages, pages and dates. It never trusts model-generated evidence
text, actor indexes, links or page numbers. This keeps CPU output small and makes
unsupported paraphrases impossible at the answer boundary.

## Isolated public-order demo (no registered records changed)

`demo-jobs.json` has five synthetic demo GUIDs and real public multi-order
matter URLs. Those GUIDs are NOT Court register records. Do not copy demo files
onto real case IDs. PDFs stay temporary; outputs stay in a separate local root.

```powershell
New-Item -ItemType Directory C:\LAC-CourtAI\demo-extraction
python tools/court-order-intelligence/worker.py --jobs tools/court-order-intelligence/demo-jobs.json --extraction-root C:\LAC-CourtAI\demo-extraction --model-version Qwen3-1.7B-Q4_K_M-b11321
python tools/court-order-intelligence/serve_questions.py --extraction-root C:\LAC-CourtAI\demo-extraction --model-version Qwen3-1.7B-Q4_K_M-b11321 --demo

cd src/LAC.Web
npm ci
npx vite --config vite.court-demo.config.ts
```

Open `http://127.0.0.1:5176/court-intelligence-demo.html`. Explicit demo config
proxies only to the isolated loopback question/artifact runtime, not the office
API/database. The normal application route/proxy and premium DHC modal are
unchanged. This demo page is not a production application route/build entry.
Some real orders have scanned annexes and correctly produce NeedsSourceReview;
some older direct links may be unavailable. Neither is a successful extraction.

## Stop

```powershell
.\scripts\stop-court-intelligence.ps1 -RuntimeDirectory C:\LAC-CourtAI\runtime -Role court-intelligence-worker
.\scripts\stop-court-intelligence.ps1 -RuntimeDirectory C:\LAC-CourtAI\runtime -Role court-intelligence-questions
.\scripts\stop-court-intelligence.ps1 -RuntimeDirectory C:\LAC-CourtAI\runtime -Role court-local-model
```

PID/start-time/executable checks prevent stopping unrelated processes. Worker
stop is cooperative between bounded operations; temporary source cleanup runs
before exit. Foreground demo: Ctrl+C also cleans temporary PDFs. Stop the worker
before the model. Do not use these scripts to stop IIS/PostgreSQL/API/frontend.

## Tests

```powershell
python -m unittest discover -s tools/court-order-intelligence -p "test_*.py" -v
dotnet test tests/LAC.Tests/LAC.Tests.csproj -c Release
cd src/LAC.Web
node --test tests/*.test.mjs
npx vite build
```

Study metadata can be regenerated using `study_corpus.py` followed by
`compile_corpus.py` against a temporary research cache outside Git. Only the
annotated metadata/URLs/hashes belong in the committed manifest, never PDFs or
full native text.
