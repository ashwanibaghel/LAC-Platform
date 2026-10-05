# Office Court runtime deployment

This automation preserves backend `2676b70`, final Court UI `9e6a715` and verified
transfer tooling `bff8803`. It configures the accepted
`Pilot-V3-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321` runtime. Installation and
verification never start the model or perform inference. No IIS publish, database
migration, PDF download, V3 reprocessing or cloud fallback is included.

## Prerequisites

Run elevated **Windows PowerShell 5.1 or PowerShell 7** on the office PC. The reviewed
branch must be checked out at `C:\LAC-Platform`; the already-deployed backend must
support the accepted recovery contract. IIS uses `DefaultAppPool` with
`ApplicationPoolIdentity`. A different existing pool can be supplied using
`-AppPool`; custom service-account identities require explicit administrator ACL
provisioning and are refused by this automatic installer.

Provision an existing all-users **x64 Python 3.10+** with PyMuPDF 1.24–<2,
requests 2.32–<3, jsonschema 4.23–<5 and psutil. Python must be readable/executable
by the app-pool identity. Nothing is downloaded or installed. The installer uses
the existing `CourtRuntime__PythonExecutable`, then `python.exe` on PATH; use
`-PythonExecutable C:\Python311\python.exe` if discovery is unavailable.

The existing effective `Storage__ExtractionRoot` must already exist and be
accessible to the app pool. It is resolved from IIS/app-pool/machine environment
and ASP.NET environment-specific JSON; the operator's user environment is ignored.
If unconfigured, the existing `C:\LAC-Publish\App_Data\extraction` is preserved.
The installer refuses a missing root; it never creates an alternate empty cache.
Keep all office document/extraction/backup storage as already configured.

## Directories and commands

| Directory | Purpose |
|---|---|
| `C:\LAC-Platform` | Reviewed repository and scripts |
| `C:\LAC-Publish` | Existing application; only Court env keys in `web.config` change |
| `D:\LAC-CourtAI-Incoming` | Four verified transfer parts and transfer manifest |
| `D:\LAC-CourtAI-V3-Office` | Exact restored model, LoRA, executable, runtime files and manifests |
| `D:\LAC-CourtAI-Runtime\package` | Verified immutable Python snapshot |
| `D:\LAC-CourtAI-Runtime\services` | Accepted controller's recovery record, locks, PID fields and stdout/stderr |
| `D:\LAC-CourtAI-Runtime\backups` | Protected original `web.config`; can contain secrets, never transfer/commit |

From an elevated office PowerShell, install in one command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LAC-Platform\scripts\install-office-court-ai.ps1 -RestoreIncoming
```

`-RestoreIncoming` invokes the accepted checksum-verified restore only when the
model directory is absent, using trusted transfer-manifest SHA
`fe212ae33eaa44ded1c3898c51409f1e4f75592d42db283b554c1250d431551d`.
An existing deployment is verified, never overwritten. Restore needs approximately
5.1 GiB additional free space beyond incoming parts. A restored deployment uses
approximately 2.4 GiB; the runtime snapshot is small. Repeating an unchanged install
verifies identity/configuration and makes no changes. Conflicting receipts or
partial installs fail closed.

When ready to load local services, explicitly run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LAC-Platform\scripts\start-office-court-services.ps1
```

This calls **only the pinned accepted `runtime_recovery.py`**. It starts 8097
independently, then attempts 8096 with the accepted CPU settings (3072 context,
6 threads, parallel 1, GPU layers 0). Historical 4096/2 manifest metadata is not
used for startup. No model request or inference is sent. The existing controller
owns the OS lock and progress record; repeated requests reuse that lock. The
already-accepted application recovery action can invoke the same controller.
No automatic boot task or permanently resident model is installed.

Verify in one command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\LAC-Platform\scripts\verify-office-court-ai.ps1
```

Verification rehashes all binaries/manifests and Python files, verifies application
configuration, checks loopback-only listener owners and exact process arguments,
checks service health and the canonical-root fingerprint. It prints a concise JSON
receipt without credentials, Court evidence, process command lines or raw logs.
Exit 0 means services ready; 2 means model offline/starting (cached evidence can
remain usable); 3 means question service offline. Configuration/hash failures
terminate with an error. `ModelInsufficientMemory` takes precedence over generic
`ModelOffline`, while reporting 8097 independently. On an approximately 8 GB PC,
the accepted model may fail allocation: close unused heavy programs and explicitly
retry recovery. Settings/weights are never reduced or changed automatically.

Default cache diagnostics prove local file readability, **not source validity**.
For authoritative cached intelligence verification, add
`-VerifyCaseId 665ec7ce-c31b-4320-9c74-45b69e14327a` to the verification command.
The script securely prompts for the officer's normal LAC login, obtains an
in-memory session cookie from local `/api/auth/login`, then performs the
authenticated, read-only localhost intelligence GET. The API's unchanged
source/claim validation must return usable briefs. This can succeed while 8096 is
offline. Credentials/cookies are never stored or printed; no model call, refresh
or PDF fetch occurs.

## Changes and rollback

Installation writes only the `CourtRuntime__` recovery paths/hash pins/version and
the **same resolved** `Storage__ExtractionRoot` into `C:\LAC-Publish\web.config`.
Unrelated settings/secrets are preserved. Updating this IIS configuration can
recycle the existing application; the script never starts/stops/publishes IIS.
Protected state contains `OFFICE-RUNTIME-INSTALL.json`, install lock, Python
snapshot, service directory and the original web-config backup. Administrators and
SYSTEM control the state; the app pool can read the snapshot and modify only
service state/logs. The original binary package and document cache are preserved.

For rollback, inspect the receipt locally and compare current `web.config` SHA256
with its `installedWebConfigSha256`. If equal, verify the backup against
`originalWebConfigSha256` and restore it using a same-directory temporary file and
atomic replacement. If other administrators have changed configuration, restore
only these Court keys from the protected backup; **do not overwrite their changes**.
No automatic uninstall deletes models, logs or accepted cached intelligence.
To fully retire runtime processes, first verify their executable, arguments and
recorded start time, then stop only those matching processes. After configuration
rollback and stopping them, an administrator can archive the runtime directory.
Never delete or relocate the canonical extraction root. A failed partial install
retains its protected backup/snapshot for diagnosis rather than guessing a repair.

Git deliverables: three entry-point scripts, shared verification helpers, accepted
Python hash inventory, focused script tests, this guide and the small existing
notice precedence correction. Court source validation, Fast Path, Q&A semantics,
model manifest/weights and inference settings remain unchanged.
