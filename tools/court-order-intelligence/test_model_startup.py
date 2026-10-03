"""Startup tests mock process creation; no server/model is launched."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/start-court-local-model.ps1"


@unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "Windows PowerShell required")
class StartupTests(unittest.TestCase):
    def run_script(self, lora=False, missing=False, manifest_bad=False, manifest_valid=False):
        with tempfile.TemporaryDirectory(prefix="lac-model-startup-") as directory:
            root = Path(directory)
            server, model, adapter = root / "server.cmd", root / "base.gguf", root / "lora.gguf"
            server.write_text("@echo off\necho Test server version\nexit /b 0\n")
            model.write_bytes(b"base fixture, not weights")
            if not missing:
                adapter.write_bytes(b"lora fixture, not weights")
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"baseGgufSha256": hashlib.sha256(model.read_bytes()).hexdigest() if manifest_valid else "wrong",
                                           "loraGgufSha256": hashlib.sha256(adapter.read_bytes()).hexdigest() if lora and not missing else None,
                                           "serverSha256": hashlib.sha256(server.read_bytes()).hexdigest(),
                                           "modelVersion": "Verified test pilot", "contextSize": 4096}))
            env = {**os.environ, "LAC_TEST_SERVER": str(server), "LAC_TEST_MODEL": str(model),
                   "LAC_TEST_LORA": str(adapter), "LAC_TEST_SCRIPT": str(SCRIPT),
                   "LAC_TEST_RUNTIME": str(root / "runtime"), "LAC_TEST_MANIFEST": str(manifest)}
            command = """
$ErrorActionPreference = 'Stop'
function Get-NetTCPConnection { return $null }
function Start-Process {
    param($FilePath,$ArgumentList,$WorkingDirectory,$WindowStyle,[switch]$PassThru,$RedirectStandardOutput,$RedirectStandardError)
    $global:testArguments = $ArgumentList
    return [pscustomobject]@{ Id=99999; StartTime=[datetime]::UtcNow }
}
$parameters = @{ ServerExe=$env:LAC_TEST_SERVER; ModelPath=$env:LAC_TEST_MODEL; RuntimeDirectory=$env:LAC_TEST_RUNTIME }
"""
            if lora:
                command += "$parameters.LoraPath=$env:LAC_TEST_LORA\n"
            if manifest_bad or manifest_valid:
                command += "$parameters.ModelManifestPath=$env:LAC_TEST_MANIFEST\n"
            command += """
try {
    & $env:LAC_TEST_SCRIPT @parameters | Out-Null
    $record = Get-Content (Join-Path $env:LAC_TEST_RUNTIME 'court-local-model.pid.json') -Raw | ConvertFrom-Json
    @{arguments=$global:testArguments;record=$record} | ConvertTo-Json -Depth 6
} catch {
    @{failed=$true;launched=($null -ne $global:testArguments)} | ConvertTo-Json
}
"""
            result = subprocess.run([shutil.which("pwsh"), "-NoProfile", "-Command", command],
                                    env=env, text=True, capture_output=True, check=True, timeout=30)
            return json.loads(result.stdout)

    def test_no_lora_backward_compatible_and_loopback_only(self):
        result = self.run_script()
        self.assertNotIn("--lora", result["arguments"])
        for value in ("--host 127.0.0.1", "--ctx-size 4096", "--parallel 1", "--n-gpu-layers 0"):
            self.assertIn(value, result["arguments"])
        self.assertIsNone(result["record"]["loraAdapter"])
        self.assertEqual("127.0.0.1", result["record"]["host"])

    def test_lora_absolute_path_hash_and_provenance(self):
        result = self.run_script(lora=True)
        self.assertIn("--lora", result["arguments"])
        adapter = result["record"]["loraAdapter"]
        self.assertTrue(Path(adapter["path"]).is_absolute())
        self.assertEqual(hashlib.sha256(b"lora fixture, not weights").hexdigest(), adapter["sha256"])
        self.assertEqual("Test server version", result["record"]["serverVersion"])

    def test_missing_lora_fails_before_process_creation(self):
        self.assertEqual({"failed": True, "launched": False}, self.run_script(lora=True, missing=True))

    def test_manifest_mismatch_fails_before_process_creation(self):
        self.assertEqual({"failed": True, "launched": False}, self.run_script(manifest_bad=True))

    def test_valid_manifest_preserves_verified_model_version(self):
        for lora in (False, True):
            result = self.run_script(lora=lora, manifest_valid=True)
            self.assertEqual("Verified test pilot", result["record"]["modelVersion"])


if __name__ == "__main__":
    unittest.main()
