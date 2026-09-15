import json
import sys
from pathlib import Path

import fitz
from PIL import Image
from rapidocr import EngineType, RapidOCR

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools" / "document-intelligence-worker"))
sys.path.insert(0, str(ROOT / "tools" / "document-intelligence-worker" / "benchmark"))
from worker import ocr_words
from nm_semantics import NmToken, detect_column_schema

PDF = ROOT / "src" / "LAC.Api" / "App_Data" / "documents" / "1cc7703d86c848e6a458a2782f819ca6-NM Pochanpur 30, 2002-04.pdf"
PAGES = (1, 4, 10, 20, 30, 60)
ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
document = fitz.open(PDF)
result = []
for page in PAGES:
    pix = document[page - 1].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
    image = Image.frombytes("RGB", [pix.width, pix.height], pix.samples).rotate(90, expand=True)
    words = ocr_words(ocr(image))
    tokens = [NmToken(page, word.text, word.bounding_box.x, word.bounding_box.y, word.bounding_box.width, word.bounding_box.height, word.confidence) for word in words]
    schema = detect_column_schema(page, tokens, image.width)
    selected = [] if schema is None else [
        {"text": token.text, "x": token.x, "y": token.y, "column": schema.column_for(token)}
        for token in tokens if any(char.isdigit() for char in token.text) or "kita" in token.text.lower()
    ]
    result.append({"page": page, "bands": [] if schema is None else schema.bands, "tokens": selected})
Path(__file__).with_name("nm-semantic-token-diagnostic.json").write_text(json.dumps(result, ensure_ascii=True, indent=2), encoding="utf-8")
