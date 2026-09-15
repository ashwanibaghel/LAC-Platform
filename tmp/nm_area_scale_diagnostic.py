import json
import sys
from pathlib import Path

from PIL import Image
from rapidocr import EngineType, RapidOCR

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools" / "document-intelligence-worker"))
from worker import ocr_words

SOURCE = ROOT / "tmp" / "nm-area-cell-diagnostic" / "diagnostic.json"
OUT = ROOT / "tmp" / "nm-area-cell-diagnostic" / "scale-diagnostic.json"
ocr = RapidOCR(params={"Det.engine_type": EngineType.TORCH, "Cls.engine_type": EngineType.TORCH, "Rec.engine_type": EngineType.TORCH})
report = []
for page in json.loads(SOURCE.read_text(encoding="utf-8")):
    for parcel in page["parcels"]:
        image = Image.open(SOURCE.parent / parcel["crop"]["file"])
        item = {"page": page["page"], "khasra": parcel["khasra"], "crop": parcel["crop"], "observations": []}
        for scale in (1, 2):
            raster = image if scale == 1 else image.resize((image.width * 2, image.height * 2), Image.Resampling.LANCZOS)
            words = ocr_words(ocr(raster))
            item["observations"].append({"scale": scale, "tokens": [{"text": word.text, "x": word.bounding_box.x / scale + parcel["crop"]["x"], "y": word.bounding_box.y / scale + parcel["crop"]["y"], "width": word.bounding_box.width / scale, "height": word.bounding_box.height / scale, "confidence": word.confidence} for word in words]})
        report.append(item)
OUT.write_text(json.dumps(report, indent=2, ensure_ascii=True), encoding="utf-8")
