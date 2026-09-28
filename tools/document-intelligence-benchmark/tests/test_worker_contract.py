import hashlib
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "document-intelligence-worker"))
from worker import add_versioned_envelope, validate_intake


class WorkerContractTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.pdf = Path(self.directory.name) / "source.pdf"
        self.pdf.write_bytes(b"fictional PDF bytes for contract-only test")
        self.sha = hashlib.sha256(self.pdf.read_bytes()).hexdigest()
        self.v2 = {
            "contractVersion": 2,
            "documentId": "11111111-1111-1111-1111-111111111111",
            "documentVersion": 3,
            "physicalSha256": self.sha,
            "pageCount": 5,
        }

    def tearDown(self):
        self.directory.cleanup()

    def test_v1_result_stays_unchanged(self):
        result = {"contractVersion": 1, "candidates": [{"candidateType": "Claim"}]}
        self.assertIsNone(validate_intake({"contractVersion": 1}, self.pdf))
        self.assertIs(add_versioned_envelope(result, {"contractVersion": 1}, None, 5), result)
        self.assertEqual({"contractVersion": 1, "candidates": [{"candidateType": "Claim"}]}, result)

    def test_v2_identity_and_envelope_round_trip_without_touching_candidates(self):
        self.assertEqual(self.sha, validate_intake(self.v2, self.pdf))
        rows = [{"candidateType": "AwardKhasra", "structuredPayload": {"khasraNumber": "7//1"}}]
        result = {"contractVersion": 2, "candidates": rows}
        add_versioned_envelope(result, self.v2, self.sha, 5)
        self.assertIs(rows, result["candidates"])
        self.assertEqual(self.sha, result["physicalSha256"])
        self.assertEqual(3, result["documentVersion"])
        self.assertEqual(5, result["pageCount"])
        self.assertTrue(result["processedAt"])
        self.assertEqual([], result["observations"])
        self.assertEqual([], result["errors"])

    def test_missing_identity_or_changed_bytes_fail_before_ocr(self):
        with self.assertRaisesRegex(ValueError, "document version"):
            validate_intake({**self.v2, "documentVersion": None}, self.pdf)
        with self.assertRaisesRegex(ValueError, "physical SHA-256"):
            validate_intake({**self.v2, "physicalSha256": None}, self.pdf)
        self.pdf.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "mismatch"):
            validate_intake(self.v2, self.pdf)

    def test_unsupported_version_fails(self):
        with self.assertRaisesRegex(ValueError, "unsupported contract"):
            validate_intake({**self.v2, "contractVersion": 3}, self.pdf)


if __name__ == "__main__":
    unittest.main()
