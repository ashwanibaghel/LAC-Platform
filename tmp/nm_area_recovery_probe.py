import json
import sys
from pathlib import Path

import fitz
from PIL import Image
from rapidocr import EngineType, RapidOCR

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools" / "document-intelligence-worker"))
from worker import ocr_words, nm_semantic_candidates

pdf = ROOT / "src" / "LAC.Api" / "App_Data" / "documents" / "1cc7703d86c848e6a458a2782f819ca6-NM Pochanpur 30, 2002-04.pdf"
pix = fitz.open(pdf)[0].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
image = Image.frombytes("RGB", [pix.width, pix.height], pix.samples).rotate(90, expand=True)
ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
items = nm_semantic_candidates(1, ocr_words(ocr(image)), image.width, image, ocr)
Path(__file__).with_name("nm-area-recovery-probe.json").write_text(json.dumps(items, ensure_ascii=True, indent=2), encoding="utf-8")
