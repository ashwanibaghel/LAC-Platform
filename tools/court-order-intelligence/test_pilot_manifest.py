"""Manifest validation fixtures use no real weights and never start inference."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from finalize_pilot_v1 import write_manifest


class ManifestTests(unittest.TestCase):
    def test_manifest_has_independently_verifiable_checksum_without_launch(self):
        with tempfile.TemporaryDirectory() as directory, patch("subprocess.check_output") as process:
            path = Path(directory) / "court-model-manifest.json"
            manifest = {"quantization": "Q4_K_M", "contextSize": 4096,
                        "modelEndpoint": "http://127.0.0.1:8096",
                        "runtimePreflightStatus": "NOT_RUN_AWAITING_RAM_CONFIRMATION",
                        "realE2EStatus": "NOT_RUN", "modelLaunchRequiresUserRamConfirmation": True}
            digest = write_manifest(path, manifest)
            self.assertEqual(manifest, json.loads(path.read_text("utf-8")))
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), digest)
            self.assertEqual(f"{digest}  {path.name}\n", Path(str(path) + ".sha256").read_text("ascii"))
            process.assert_not_called()

    def test_existing_manifest_or_checksum_is_never_overwritten(self):
        for suffix in ("", ".sha256"):
            with tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "court-model-manifest.json"
                existing = Path(str(path) + suffix)
                existing.write_bytes(b"preserved fixture")
                with self.assertRaises(ValueError):
                    write_manifest(path, {"unexpected": "replacement"})
                self.assertEqual(b"preserved fixture", existing.read_bytes())


if __name__ == "__main__":
    unittest.main()
