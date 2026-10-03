# Preserved V1 local pilot packaging / A-B fallback

The current user-selected local acceptance target is the preserved evaluated
Pilot V3 adapter; see `court-intelligence-v3-local-acceptance.md`. This V1
package is preserved unchanged as the explicit A/B fallback, not the final model.

## Interrupted conversion recovery

`recover_pilot_v1.py` audits a preserved GGUF header against pinned source names,
shapes/types, model parameters and chat template; hashes the partial; excludes
incomplete tensors; and checks finite BF16/exact F32 norm widening. The original
partial and cached ranges are never overwritten/deleted. Complete tensors are
reused without re-download.

For limited disk space, two fresh directories stage standard upstream GGUF
shards across D:/E:. Local file aliases let the unchanged pinned quantizer merge
them into a single Q4_K_M output. Synthetic tests prove identical quantized
tensor bytes for single-file and sharded inputs. New ranges stream in bounded
buffers without a full inference model or new raw-weight disk cache.

Each completed tensor has an immutable checkpoint; a 1 GiB per-drive reserve
fails closed and retains partial output. Provenance records each shard SHA256
and an explicitly labeled ordered-shard hash aggregate, not a fictitious
single-file hash. The finalizer verifies all shards before generating the
checksummed deployment manifest. Recovery does not authorize inference.

This is an experimental, manually verified officer pilot, not a production
quality promotion. Packaging does not establish inference quality or real-case
acceptance. Do not launch the model until the user confirms RAM headroom.

## Immutable inputs

- Product base: `5acc2d2f7c928228a7972264e0ca68c173531b99`.
- Base: `Qwen/Qwen3-4B-Instruct-2507`.
- Base revision: `cdbee75f17c01a7cc42f958dc650907174af0554`.
- Evaluated V1 PEFT adapter SHA256:
  `09ebbd176cbdf0390bee3e47343b351da7a2fe80c72a8837e7de7f6eb291fb67`.
- llama.cpp b11321: `b0aca3c6539e2dd55ea510bbb79591852a4d4b81`.
- Official Windows CPU ZIP SHA256:
  `8f8c0c6501b075f52deff59537c05acd57d8621a0a7935f29b7d7c4812892569`.

Do not substitute the older 1.7B runtime model, smoke adapters or V2 for this
V1 fallback. V3 acceptance uses its separately checksummed package, never a
renamed V1 adapter.

## Method B

Keep the V1 LoRA separate. Convert the exact pinned base to BF16 GGUF, then
quantize that base once to Q4_K_M. Convert the adapter to F32 GGUF and verify
every converted tensor is bit-exact with the original V1 adapter. No training,
adapter merge, adapter quantization or full model inference is involved.

The repository helper imports the unmodified, pinned upstream converter. Its
remote reader uses the immutable revision instead of upstream remote mode's
`main`. Config files are checked against pinned Git/LFS identities. Each tensor
read checks HTTP 206, exact Content-Range and exact byte length. A conversion
receipt records all consumed source tensor SHA256 values. Publisher whole-file
SHA256 values are labeled as publisher metadata, not falsely claimed as local
whole-source-file hash verification.

Large tensor downloads use eight bounded 4 MiB ranges, a disk-backed tensor
buffer and checksum-verified range caching. Transport errors have three bounded
attempts; integrity errors fail immediately. The upstream converter transforms
one tensor at a time (not a full inference runtime); tensor conversion can still
need additional temporary memory. No model server is started by this pipeline.

All weights, temporary GGUF files, converter dependencies and runtime logs stay
outside Git. Use local disks, not a cloud-synchronized model directory. Keep
enough free disk for an approximately 8 GB BF16 intermediate and the Q4 output.
Do not delete existing user artifacts to obtain space.

## Reproduce without launching a model

1. Verify the clean upstream converter checkout and the official CPU ZIP hash.
2. Run `package_pilot_v1.py` with the preserved V1 root, converter checkout,
   pinned config directory and a **new** BF16 output path. It verifies the
   complete 44-entry V1 artifact manifest and refuses to overwrite a GGUF.
3. Run upstream `convert_lora_to_gguf.py --outtype f32 --base <config-only-dir>
   --outfile <new-lora.gguf> <verified-v1-adapter-dir>`. The config-only directory
   contains the verified pinned config/tokenizer files but no weight index;
   otherwise this upstream version tries to open base weight shards unnecessarily.
   Use offline Hugging Face/Transformers settings for this adapter conversion.
4. Run `llama-quantize.exe --max-buffer-size 128 <bf16.gguf> <new-q4.gguf>
   Q4_K_M 1`. This bounds the quantizer buffer and uses one CPU thread. Do not
   use `--allow-requantize` or an unrelated importance matrix.
5. Run `finalize_pilot_v1.py` against the base, LoRA, conversion provenance,
   verified V1 root, converter and server executable. It validates GGUF metadata,
   288 exact adapter tensors and runtime version before emitting a manifest and
   its `.sha256` sidecar. It also verifies the downloaded official runtime ZIP
   and every extracted runtime file.

The manifest contains file hashes and filenames, not credentials, raw Court
content or private training paths. Its acceptance fields remain `NOT_RUN` until
the actual local preflight and real-case acceptance are performed.

## After explicit RAM confirmation only

Use `scripts/start-court-local-model.ps1` with `-ServerExe`, `-ModelPath`,
`-LoraPath`, `-ModelManifestPath` and an absolute `-RuntimeDirectory`. The script
checks base/adapter/server hashes before launching, records local path/hash/version
provenance, and preserves no-LoRA compatibility.

Runtime invariants: literal `127.0.0.1:8096`, context 4096, two CPU threads,
parallel 1, GPU layers 0. Questions use literal `127.0.0.1:8097`, the existing
absolute extraction root and **no Demo**. There is no cloud inference fallback.

Do not run API contract tests, real PDF processing, Q&A or E2E acceptance merely
because packaging succeeded. First obtain the user's RAM confirmation, then
measure actual runtime memory and latency. Do not publish/swap IIS or merge main
until real-source acceptance passes.
