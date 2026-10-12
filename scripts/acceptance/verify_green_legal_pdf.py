"""Read-only geometry gate for clean Green Legal feasibility PDFs, not note conversion.

Requires pdfplumber and pypdfium2. Uses rasterized ink, rather than font em boxes,
to measure printed clearance. Does not certify official submission or editor leases.
"""
import argparse
import gc
import hashlib
import json
from pathlib import Path


def geometry_failures(row):
    failures = []
    if abs(row['widthPt'] - 612) > .01 or abs(row['heightPt'] - 1008) > .01:
        failures.append('Legal MediaBox must be 612 x 1008 pt')
    # DOCX uses integer twips. Allow one twip (0.018 mm), not a typography-sized error.
    tolerance = .02
    minimums = dict(left=45 if row['page'] % 2 else 15,
                    right=15 if row['page'] % 2 else 45, top=25, bottom=25)
    for side, minimum in minimums.items():
        if row['inkClearanceMm'][side] < minimum - tolerance:
            failures.append(f'{side} ink clearance is below {minimum} mm')
    return failures


def neutral_color(color):
    if color is None:
        return True
    if isinstance(color, (int, float)):
        return True  # DeviceGray
    if not isinstance(color, (tuple, list)):
        return False  # Unknown color space cannot silently pass.
    if len(color) == 1:
        return True
    if len(color) == 3:
        return max(color) - min(color) < .000001
    if len(color) == 4:
        return max(color[:3]) < .000001  # Neutral CMYK without colored inks
    return False


def inspect(source, dpi=600):
    import pdfplumber
    import pypdfium2 as pdfium
    rows, failures = [], []
    document = pdfium.PdfDocument(str(source))
    with pdfplumber.open(source) as parsed:
        for index, page in enumerate(parsed.pages):
            pdf_page = document[index]
            bitmap = pdf_page.render(scale=dpi / 72, grayscale=True)
            image = bitmap.to_pil().convert('L')
            # Include even pale antialias pixels conservatively in the ink rectangle.
            mask = image.point(lambda p: 255 if p < 255 else 0)
            bounds = mask.getbbox()
            if bounds is None:
                failures.append(f'page {index+1}: blank page is unexpected in this fixture')
                bounds = (0, 0, image.width, image.height)
            x0, y0, x1, y1 = bounds
            mm_per_pixel = 25.4 / dpi
            chars = page.chars
            row = dict(page=index+1, widthPt=page.width, heightPt=page.height,
                       rasterDpi=dpi, inkPixelBounds=list(bounds),
                       inkClearanceMm=dict(left=x0*mm_per_pixel, top=y0*mm_per_pixel,
                                           right=(image.width-x1)*mm_per_pixel,
                                           bottom=(image.height-y1)*mm_per_pixel),
                       fontBoxBottomMm=(page.height-max(c['bottom'] for c in chars))*25.4/72 if chars else None)
            row['failures'] = geometry_failures(row)
            # Raster grayscale measures geometry only. Inspect original color operators
            # independently so grayscale rendering cannot hide red/green decorations.
            objects = chars + page.rects + page.lines + page.curves
            colored = [o for o in objects if any(not neutral_color(o.get(k))
                       for k in ('stroking_color', 'non_stroking_color'))]
            row['nonNeutralPaintObjects'] = len(colored)
            row['images'] = len(page.images)
            if colored:
                row['failures'].append('colored paint, including possible red/green decoration')
            if page.images:
                row['failures'].append('image colors require separate verification')
            failures.extend(f'page {index+1}: {f}' for f in row['failures'])
            rows.append(row)
            mask.close()
            image.close()
            bitmap.close()
            pdf_page.close()
            gc.collect()
    document.close()
    return dict(pdfSha256=hashlib.sha256(source.read_bytes()).hexdigest(), pages=rows,
                passed=not failures, failures=failures,
                scope='Printed fixture geometry only; no official-noting or editor-session certification',
                metric='Rasterized visible ink; font boxes reported separately, never treated as ink')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pdf', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.pdf)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    raise SystemExit(0 if report['passed'] else 1)


if __name__ == '__main__':
    main()
