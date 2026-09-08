"""Formatting-only area normalization and geometry-based crop safeguards."""
import re
from .specialist_grammar_v6 import valid_area

AREA_FORMAT=re.compile(r'^\s*(\d{1,3})\s*(?:[-–—]\s*)+\s*(\d{1,2})\s*$')
def normalize_area_evidence(raw):
    """Return preserved OCR and a value only when every digit remains visible."""
    raw=str(raw or ''); m=AREA_FORMAT.fullmatch(raw)
    if not m: return {'rawOcrText':raw,'normalizedValue':None,'normalizationReason':None,'status':'NeedsReview'}
    value=f'{m.group(1)}-{m.group(2)}'
    return {'rawOcrText':raw,'normalizedValue':value,'normalizationReason':'AreaSeparatorNormalization' if raw.strip()!=value else None,'status':'Valid' if valid_area(value) else 'NeedsReview'}
def token_belongs_to_cell(token,cell):
    """Accept a token only when its centre is inside the authoritative cell."""
    cx=token['x']+token['width']/2; cy=token['y']+token['height']/2
    return cell['x'] <= cx <= cell['x']+cell['width'] and cell['y'] <= cy <= cell['y']+cell['height']
def inner_cell_box(cell,fraction=.06):
    """Conservative inner geometry: edge-only inset, never string deletion."""
    dx=cell['width']*fraction; dy=cell['height']*fraction
    return {'x':cell['x']+dx,'y':cell['y']+dy,'width':cell['width']-2*dx,'height':cell['height']-2*dy}
def contamination_status(outer,inner,role):
    outer_value=normalize_area_evidence(outer)['normalizedValue'] if role=='area' else ' '.join(str(outer).split())
    inner_value=normalize_area_evidence(inner)['normalizedValue'] if role=='area' else ' '.join(str(inner).split())
    if inner_value and outer_value!=inner_value: return 'ContaminationRecovered',outer_value,inner_value
    return 'Consistent' if inner_value==outer_value else 'NeedsReview',outer_value,inner_value
