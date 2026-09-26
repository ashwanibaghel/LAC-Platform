import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from benchmark.worker_semantics import award_candidate

spec = importlib.util.spec_from_file_location("award_worker", ROOT.parent / "document-intelligence-worker" / "worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class AwardReviewEvidenceTests(unittest.TestCase):
    def test_disagreement_is_evidence_and_does_not_repair_identifier(self):
        self.assertEqual(worker.reading_agreement(["22//2/7", "22//2/1"]), "OcrDisagreement")
        cells = {
            0: {"text": "22//2/7", "cellCropOcr": "22//2/1", "multiViewPredictions": [{"view": "upscale2", "rawPrediction": "22//2/1"}], "recognitionAgreement": "OcrDisagreement", "region": {"x": 1, "y": 1, "width": 10, "height": 10}},
            1: {"text": "4-16", "region": {"x": 12, "y": 1, "width": 10, "height": 10}},
            2: {"text": "4-10", "region": {"x": 24, "y": 1, "width": 10, "height": 10}},
        }
        candidate = award_candidate(43, 1, 2, cells, {"khasra": 0, "recordedArea": 1, "awardedArea": 2})
        self.assertEqual(candidate["structuredPayload"]["khasraNumber"], "22//2/7")
        self.assertEqual(candidate["structuredPayload"]["sourceCells"]["khasra"]["recognitionAgreement"], "OcrDisagreement")
        self.assertIn("OCR readings disagree; verify each source cell visually", candidate["interpretationWarnings"])

    def test_unreadable_and_agreement_are_diagnostic_only(self):
        self.assertEqual(worker.reading_agreement([None, ""]), "Unreadable")
        self.assertEqual(worker.reading_agreement(["6//10"]), "SingleRecognizer")
        self.assertEqual(worker.reading_agreement(["6//10", "6//10"]), "StrongAgreement")

    def test_inner_crop_preserves_outer_reading_without_promoting_digits(self):
        cells = {
            0: {"text": "6//10", "region": {"x": 1, "y": 1, "width": 10, "height": 10}},
            1: {"text": "4-1|", "cellCropOcr": "4-1|", "innerCellOcr": "4-16", "contaminationStatus": "ContaminationRecovered", "region": {"x": 12, "y": 1, "width": 10, "height": 10}},
            2: {"text": "4-10", "region": {"x": 24, "y": 1, "width": 10, "height": 10}},
        }
        candidate = award_candidate(43, 1, 2, cells, {"khasra": 0, "recordedArea": 1, "awardedArea": 2})
        recorded = candidate["structuredPayload"]["sourceCells"]["recordedArea"]
        self.assertEqual(recorded["cellCropOcr"], "4-1|")
        self.assertEqual(recorded["innerCellOcr"], "4-16")
        self.assertIsNone(recorded["normalizedSuggestion"])
        self.assertIn("Inner cell reading suggests border contamination; both readings require human verification", candidate["interpretationWarnings"])


if __name__ == "__main__":
    unittest.main()
