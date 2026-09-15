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
from worker import ocr_words, nm_semantic_candidates
from nm_semantics import NmToken, detect_column_schema

PDF = ROOT / "src" / "LAC.Api" / "App_Data" / "documents" / "1cc7703d86c848e6a458a2782f819ca6-NM Pochanpur 30, 2002-04.pdf"
OUT = ROOT / "tmp" / "nm-area-diagnostic"
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
    candidates = nm_semantic_candidates(page, words, image.width)
    parcels = [parcel for candidate in candidates if candidate["candidateType"] == "NmSemanticOwnerBlock" for parcel in candidate["structuredPayload"]["parcels"]]
    item = {"page": page, "areaBand": None if schema is None else next(({"left": left, "right": right} for role, left, right in schema.bands if role == "area"), None), "fullPageAreaTokens": [], "parcels": []}
    if schema:
        item["fullPageAreaTokens"] = [{"text": token.text, "x": token.x, "y": token.y, "width": token.width, "height": token.height, "confidence": token.confidence} for token in tokens if schema.column_for(token) == "area"]
    for index, parcel in enumerate(parcels, 1):
        source = parcel["khasraSource"]["sourceRegion"]
        band = item["areaBand"]
        if band is None:
            continue
        pad = 6
        left, right = max(0, int(band["left"] - pad)), min(image.width, int(band["right"] + pad))
        top, bottom = max(0, int(source["y"] - pad)), min(image.height, int(source["y"] + source["height"] + pad))
        crop = image.crop((left, top, right, bottom))
        crop_path = OUT / f"page-{page}-parcel-{index}.png"
        crop.save(crop_path)
        crop_words = ocr_words(ocr(crop))
        item["parcels"].append({"rawKhasraText": parcel["rawKhasraText"], "fullPageArea": parcel["rawAreaText"], "khasraSource": source, "crop": {"x": left, "y": top, "width": right-left, "height": bottom-top, "file": crop_path.name}, "cropTokens": [{"text": word.text, "x": word.bounding_box.x + left, "y": word.bounding_box.y + top, "width": word.bounding_box.width, "height": word.bounding_box.height, "confidence": word.confidence} for word in crop_words]})
    report.append(item)
(OUT / "diagnostic.json").write_text(json.dumps(report, indent=2, ensure_ascii=True), encoding="utf-8")
