from __future__ import annotations

from pathlib import Path
import json

import cv2
import fitz
import numpy as np
from PIL import Image


PDF = Path(r"C:\LAC-Platform\src\LAC.Api\App_Data\documents\1cc7703d86c848e6a458a2782f819ca6-NM Pochanpur 30, 2002-04.pdf")
PAGES = (1, 10, 20, 30, 40, 50, 60, 70, 75)
OUTPUT = Path(r"C:\LAC-Platform\tmp\nm-feasibility")


def variants(image: Image.Image) -> dict[str, Image.Image]:
    rgb = np.array(image.convert("RGB"))
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    # Printed ink is dark with low saturation. This intentionally rejects vivid
    # handwriting but never attempts to reconstruct or replace it.
    dark_low_saturation = 255 - (((hsv[:, :, 2] < 185) & (hsv[:, :, 1] < 115)).astype(np.uint8) * 255)
    gray = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY)
    adaptive = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
                                     cv2.THRESH_BINARY, 41, 13)
    return {
        "original": image,
        "dark-low-saturation": Image.fromarray(dark_low_saturation),
        "adaptive-grayscale": Image.fromarray(adaptive),
    }


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    doc = fitz.open(PDF)
    summary: list[dict[str, object]] = []
    for number in PAGES:
        page = doc[number - 1]
        pix = page.get_pixmap(matrix=fitz.Matrix(1.5, 1.5), alpha=False)
        image = Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
        # Real NM scans are sideways. Rotate each page independently but do not
        # register, align, or compare it with any other page.
        image = image.rotate(90, expand=True)
        page_dir = OUTPUT / f"page-{number}"
        page_dir.mkdir(exist_ok=True)
        for name, variant in variants(image).items():
            variant.save(page_dir / f"{name}.png")
        summary.append({"page": number, "width": image.width, "height": image.height,
                        "rotationDegrees": 90, "variants": ["original", "dark-low-saturation", "adaptive-grayscale"]})
    (OUTPUT / "render-summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
