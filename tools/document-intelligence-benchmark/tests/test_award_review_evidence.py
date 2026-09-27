import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from benchmark.worker_semantics import award_candidate
from benchmark.normalized import BoundingBox, Word

spec = importlib.util.spec_from_file_location("award_worker", ROOT.parent / "document-intelligence-worker" / "worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class AwardReviewEvidenceTests(unittest.TestCase):
    def test_real_assigned_word_scores_are_retained_conservatively(self):
        geometry = [{"label": "table row", "box": {"x": 0, "y": y, "width": 60, "height": 10}} for y in (0, 10)]
        geometry += [{"label": "table column", "box": {"x": x, "y": 0, "width": 20, "height": 20}} for x in (0, 20, 40)]
        words = [Word("4//12", BoundingBox(1, 11, 6, 4), .999), Word("min", BoundingBox(8, 11, 6, 4), .995),
                 Word("2-2", BoundingBox(22, 11, 8, 4), .996), Word("1-1", BoundingBox(42, 11, 8, 4), .997)]
        rows, count = worker.rows_for_table(geometry, {"x": 0, "y": 0, "width": 60, "height": 20}, words, 1, 1)
        self.assertEqual(count, 3)
        self.assertEqual(rows[1][0]["confidence"], .995)
        candidate = award_candidate(1, 1, 1, rows[1], {"khasra": 0, "recordedArea": 1, "awardedArea": 2})
        self.assertEqual(candidate["confidence"], .995)
        self.assertFalse(candidate["requiresIndividualReview"])
        self.assertEqual(candidate["structuredPayload"]["sourceCells"]["recordedArea"]["pageAssignedConfidence"], .996)
        missing = [words[0], Word("min", BoundingBox(8, 11, 6, 4), None), *words[2:]]
        uncertain_rows, _ = worker.rows_for_table(geometry, {"x": 0, "y": 0, "width": 60, "height": 20}, missing, 1, 1)
        uncertain = award_candidate(1, 1, 1, uncertain_rows[1], {"khasra": 0, "recordedArea": 1, "awardedArea": 2})
        self.assertIsNone(uncertain["confidence"])
        self.assertTrue(uncertain["requiresIndividualReview"])

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
        self.assertTrue(candidate["requiresIndividualReview"])
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
