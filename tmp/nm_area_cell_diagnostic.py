import json
import sys
from pathlib import Path

import fitz
from PIL import Image
from rapidocr import EngineType, RapidOCR

ROOT = Path(__file__).resolve().parents[1]
WORKER = ROOT / "tools" / "document-intelligence-worker"
sys.path.insert(0, str(WORKER))
sys.path.insert(0, str(WORKER / "benchmark"))
from worker import ocr_words, targeted_area_crop_bounds
from nm_semantics import NmToken, detect_column_schema, semantic_owner_blocks

PDF = ROOT / "src" / "LAC.Api" / "App_Data" / "documents" / "1cc7703d86c848e6a458a2782f819ca6-NM Pochanpur 30, 2002-04.pdf"
OUT = ROOT / "tmp" / "nm-area-cell-diagnostic"
PAGES = (1, 4, 10, 20, 30, 60)
OUT.mkdir(exist_ok=True)
ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
document = fitz.open(PDF)
report = []
for page in PAGES:
    pix = document[page - 1].get_pixmap(matrix=fitz.Matrix(2, 2), alpha=False)
    image = Image.frombytes("RGB", [pix.width, pix.height], pix.samples).rotate(90, expand=True)
    words = ocr_words(ocr(image))
    tokens = [NmToken(page, word.text, word.bounding_box.x, word.bounding_box.y, word.bounding_box.width, word.bounding_box.height, word.confidence) for word in words]
    schema = detect_column_schema(page, tokens, image.width)
    if schema is None:
        report.append({"page": page, "parcels": []})
        continue
    grouped = {}
    for token in tokens:
        role = schema.column_for(token)
        if role is not None:
            grouped.setdefault((role, round(token.y / 12)), []).append(token)
    semantic = []
    for _, group in grouped.items():
        group.sort(key=lambda item: item.x)
        first = group[0]
        confidence = min((item.confidence for item in group if item.confidence is not None), default=None)
        semantic.append(NmToken(page, " ".join(item.text for item in group), first.x, min(item.y for item in group), max(item.right for item in group)-first.x, max(item.height for item in group), confidence))
    blocks = semantic_owner_blocks(semantic, schema)
    entries = []
    for block in blocks:
        for index, parcel in enumerate(block.parcels, 1):
            if parcel.raw_area is not None or parcel.khasra_token is None:
                continue
            bounds = targeted_area_crop_bounds(parcel, block.parcels, schema, image)
            if bounds is None:
                continue
            left, top, right, bottom = bounds
            crop = image.crop(bounds)
            file = f"page-{page}-parcel-{index}.png"
            crop.save(OUT / file)
            crop_words = ocr_words(ocr(crop))
            centre = parcel.khasra_token.y + parcel.khasra_token.height / 2
            entries.append({"khasra": parcel.raw_khasra, "khasraBox": parcel.khasra_token.region(), "khasraCenterY": centre, "khasraHeight": parcel.khasra_token.height, "crop": {"x": left, "y": top, "width": right-left, "height": bottom-top, "file": file}, "cropTokens": [{"text": word.text, "x": word.bounding_box.x+left, "y": word.bounding_box.y+top, "width": word.bounding_box.width, "height": word.bounding_box.height, "centerY": word.bounding_box.y+top+word.bounding_box.height/2, "confidence": word.confidence} for word in crop_words]})
    report.append({"page": page, "areaBand": next(({"left": left, "right": right} for role, left, right in schema.bands if role == "area"), None), "parcels": entries})
(OUT / "diagnostic.json").write_text(json.dumps(report, indent=2, ensure_ascii=True), encoding="utf-8")
