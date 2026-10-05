# Portable Court V3 office transfer

**Built and verified on 5 October 2026.** Transfer files are in `D:\LAC-CourtAI-Office-Transfer`. No inference, model start, requantization, weight modification or download was performed.

## Files to send through WhatsApp

Send the **four `.part0001`–`.part0004` files and `TRANSFER-MANIFEST.json` as documents**, preserving their names. The office incoming folder is `D:\LAC-CourtAI-Incoming`. The full TAR and staging payload are retained locally; they do not need to be sent separately. Code/scripts and the trusted checksum receipt move through GitHub on `codex/court-v3-office-transfer`.

| Part | Bytes | MiB | SHA256 |
| --- | ---: | ---: | --- |
| `court-v3-office.tar.part0001` | 786,432,000 | 750.000 | `c334a0ba012ab47f4cd33d1e62c32be7140b00151d93d17d828180601c6f185c` |
| `court-v3-office.tar.part0002` | 786,432,000 | 750.000 | `e730019ffffc008ec9ee54a02aae81758fd62798654a98f7f492555b0b8cdcb4` |
| `court-v3-office.tar.part0003` | 786,432,000 | 750.000 | `f7621023101103cca6608aef63977f2df608b5f447a0bbb40acd5cb45b417f3b` |
| `court-v3-office.tar.part0004` | 211,100,672 | 201.321 | `0d71276fc9659a0d6982783971a00197579d39b517af40b1b7b2d80eb6ec450e` |

Whole uncompressed USTAR: **2,570,396,672 bytes** (2.393868 GiB).

- Whole TAR SHA256: `eed1ced95df9e92114bbef506e7bc0dda6cdd3b7dc843bbc5a6534523ae4d863`
- Portable deployment manifest SHA256: `36fc8c63794e566df7fecfd2588130c9c295d145b49d7015055171f154e2fb4f`
- **Trusted TRANSFER-MANIFEST.json SHA256:** `fe212ae33eaa44ded1c3898c51409f1e4f75592d42db283b554c1250d431551d`
- Original deployment manifest SHA256: `926a2dc2002941987c40b4ddd656e5e4b12d5c5e2fa74423fb3500b8f026631b`

The transfer-manifest hash is supplied from this Git-tracked receipt; it must not be taken from an untrusted incoming file and used as its own verification anchor. Git preserves the [exact manifest receipt](receipts/court-v3-office-transfer-manifest.json) without newline conversion. All part hashes are checked before joining; the complete joined TAR is checked before extraction.

## Exact source artifacts

| Artifact | Original source | SHA256 |
| --- | --- | --- |
| Accepted Q4_K_M base | `E:\LAC-CourtAI-V1-Packaging-20261003\qwen3-4b-pinned-Q4_K_M-recovered-r1.gguf` | `9657e9d21175ed290fa4ec3662fffb61a001463ac4ec1abd62deaa509b440710` |
| Accepted V3 F32 LoRA | `D:\LAC-CourtAI-V3-20261003-r1\pilot-v3-lora-f32.gguf` | `f002c12aef178431cd56bc482350f3733e92f51972a668ed119a7d946e68daf1` |
| Accepted llama server | `D:\LAC-CourtAI-V1-20261003\bin\llama-server.exe` | `9c2cfb0c15c3acca4587aa0c877f27b342c3c4ee4e1d2a6a5e68316064142ebe` |

All **51 manifest-pinned runtime files** from the shared V1 `bin` directory are included with their original hashes, including all CPU DLL variants and the OpenMP license. The exact 53-file source inventory, byte counts and SHA256 values are in [the full receipt](receipts/court-v3-office-transfer-report.json). The portable directory also includes the checksum-pinned original manifest and portable manifest: **55 verified files total**. No secrets, documents, databases, prompts, training datasets or application artifacts are in the binary archive.

## Manifest and startup policy

The portable manifest changes only `baseGgufPath`, `loraGgufPath`, `serverPath` to absolute paths under `D:\LAC-CourtAI-V3-Office` and adds portability metadata. The original manifest is included byte-for-byte so restore can independently compare every other field. Original provenance paths remain historical metadata; no external source package is required to load the bundled binary runtime.

Model identity stays `Pilot-V3-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321`; binary hashes, endpoints and no-cloud/CPU policy stay unchanged. **Startup settings come from the accepted `tools/court-order-intelligence/runtime_recovery.py`, not the original manifest's historical 4096-context/2-thread fields.** The accepted controller SHA256 is `dd2638ed49685a4e47a2e61c91352e6f9b8d4b8353e51fa12394ddf9147c0102`. It controls 3072 context, 6 threads, parallel 1, GPU layers 0, priority -1, poll 0, f16 KV, flash attention auto, ubatch 128 and cache RAM 0. No legacy start script is included or invoked.

The Git attribute for this controller preserves its accepted Windows CRLF bytes on checkout, keeping the exact SHA256 without changing its source code or settings.

After a later authorized host configuration, ASP.NET recovery must use the **portable** manifest path/hash and matching Git Python-package/recovery-script pins. This transfer/restore does not change application config, start services or install Python. Restoring the binary package needs Windows PowerShell 5.1+ and Windows `tar.exe`; Codex is not needed on the office PC. The Git-tracked Court Python service and its installed dependencies remain part of the application runtime setup.

## Commands

Run from the Git checkout containing these scripts. The preparation command below was already executed successfully; the script intentionally refuses the existing output directory. Use a new output path for a deliberate rebuild.

Laptop:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-court-v3-office-transfer.ps1 -OutputDirectory "D:\LAC-CourtAI-Office-Transfer"
```

Office, after placing all four parts and `TRANSFER-MANIFEST.json` in the incoming directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\restore-court-v3-office-transfer.ps1 -IncomingDirectory D:\LAC-CourtAI-Incoming -ExpectedTransferManifestSha256 fe212ae33eaa44ded1c3898c51409f1e4f75592d42db283b554c1250d431551d
```

The office script rejects an existing destination, verifies all binaries and both manifests in a newly created staging folder, then uses a same-volume directory rename to install atomically. A race creating the destination also fails rather than overwriting it. `INSTALL-RECEIPT.json` records hashes and `modelStarted=false`. The joined TAR is removed after successful installation; incoming parts are retained. Failure leaves new staging for diagnosis and does not silently replace any installed deployment.

## Disk requirement

- Installed binaries: **2,570,343,645 bytes** (2.394 GiB), plus small manifests/receipt.
- Preparation reserves payload + TAR + parts + 256 MiB: approximately **7.44 GiB free**; use **8 GiB free** as a practical minimum.
- Office peak including received parts, joined TAR, staging payload and reserve: **7,979,572,445 bytes** (7.432 GiB).
- With incoming parts already present, restore requires additional free space for TAR + extracted payload + reserve, approximately **5.04 GiB**. After success the joined TAR is deleted; installed runtime plus retained incoming parts use approximately **4.79 GiB**.

## Checks

**23 focused assertions passed on both Windows PowerShell 5.1 and PowerShell 7.** They cover wrong/missing parts, wrong whole archive/model hashes, existing destination refusal, runtime-policy mutation, output/source overlap, unsafe TAR traversal/extra members, deterministic joining and no model process launch. The [independent audit](receipts/court-v3-office-transfer-audit.json) passed for every archive member's bytes/hash, every transport part, the joined-stream checksum, staged payload and unchanged original source hashes. A Windows Git checkout with `core.autocrlf=true` also reproduced the accepted controller hash exactly.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\test-court-v3-office-transfer.ps1
```

For local disk space, the user authorized temporary-file cleanup. Expired June Windows upgrade cache images under `D:\$WINDOWS.~TMP` were removed after confirming completed setup, no rollback registration, pending reboot or active setup/update process. Protected leftover directories remain; model source packages, active API files, Drive upload cache, projects and office documents were preserved.
