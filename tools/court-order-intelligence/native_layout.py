"""Native-PDF structural paragraph boundaries; no judicial facts or OCR.

Only a unique consecutive run of standalone numbers at the document's outer
paragraph margin can reset a leaked quote/speaker frame. Indented quotations,
dates, conflicting numbering and ambiguous text matches provide no reset proof.
"""
import re
import fitz
from semantics import normalized
from anchors import source_header


def outer_paragraph_offsets(path, pages):
    _, bodies = source_header(pages)
    if bodies is None:
        return {}
    numbered = []
    with fitz.open(path) as document:
        for page_number, page in enumerate(document, 1):
            if page_number not in bodies:
                continue
            lines = [line for block in page.get_text('dict', sort=True)['blocks']
                     if 'lines' in block for line in block['lines']]
            for index, line in enumerate(lines):
                text = normalized(''.join(span['text'] for span in line['spans']))
                match = re.fullmatch(r'(\d{1,3})\.', text)
                if not match:
                    continue
                # PDF label and paragraph text commonly occupy separate lines
                # at the same y. Preserve order supplied by native extraction.
                following = ' '.join(normalized(''.join(s['text'] for s in item['spans']))
                                     for item in lines[index+1:index+4])
                prefix = normalized(text+' '+following)[:70]
                body = bodies[page_number]
                if len(prefix) < 15 or body.count(prefix) != 1:
                    continue
                numbered.append((page_number, line['bbox'][0], int(match[1]), body.index(prefix)))
    if not numbered:
        return {}
    margin = min(item[1] for item in numbered)
    outer = [item for item in numbered if abs(item[1]-margin) <= 1]
    # All-or-nothing certificate. Never treat left-aligned quoted numbering as
    # an outer return if the numbering run is interrupted or duplicated.
    if len(outer) < 3 or any(b[2] != a[2]+1 for a, b in zip(outer, outer[1:])):
        return {}
    result = {}
    for page, _, number, offset in outer:
        result.setdefault(page, []).append((offset, number))
    return result
