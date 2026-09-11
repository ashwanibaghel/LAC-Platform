"""Deterministic, evidence-first semantics for NM registers.

Every observation retains its own source token. This module has no database
dependency and never fills a source value from the Village master.
"""
from __future__ import annotations
from dataclasses import dataclass, field
import re
from typing import Iterable

@dataclass(frozen=True)
class NmToken:
    page: int; text: str; x: float; y: float; width: float; height: float
    confidence: float | None = None; column: str | None = None
    @property
    def right(self) -> float: return self.x + self.width
    def region(self) -> dict: return {"x": self.x, "y": self.y, "width": self.width, "height": self.height, "rotationDegrees": 90}

@dataclass(frozen=True)
class NmColumnSchema:
    page: int; bands: tuple[tuple[str, float, float], ...]; confidence: str
    def column_for(self, token: NmToken) -> str | None:
        centre = token.x + token.width / 2
        return next((role for role, left, right in self.bands if left <= centre < right), None)

@dataclass
class NmParcel:
    raw_khasra: str | None = None; raw_area: str | None = None; land_class: str | None = None
    khasra_token: NmToken | None = None; area_token: NmToken | None = None; land_class_token: NmToken | None = None
    inherited: bool = False

@dataclass
class NmOwnerBlock:
    page: int; sequence: int; recorded_name: str | None = None; parentage: str | None = None
    residence: str | None = None; share_raw: str | None = None
    name_token: NmToken | None = None; parentage_token: NmToken | None = None; residence_token: NmToken | None = None; share_token: NmToken | None = None
    parcels: list[NmParcel] = field(default_factory=list)
    components: dict[str, tuple[str, NmToken]] = field(default_factory=dict)
    exceptions: list[str] = field(default_factory=list)
    ditto_token: NmToken | None = None; inherited_from_sequence: int | None = None
    source_tokens: list[NmToken] = field(default_factory=list)
    @property
    def auto_structured(self) -> bool:
        return bool(self.recorded_name and self.name_token and self.parcels and all(p.khasra_token and p.area_token for p in self.parcels) and not self.exceptions)

# Specific labels precede their broad suffixes: grand total must not be
# consumed as total and structure compensation must not become land compensation.
_LABELS = (
    ("grand_total", ("grand total",)), ("additional_compensation", ("additional compensation", "23(1a)")),
    ("structure_compensation", ("structure compensation",)), ("land_compensation", ("land compensation",)),
    ("base_total", ("base total", "total")), ("solatium", ("solatium",)), ("interest", ("interest", "u/s 34")),
    ("land_class", ("land class", "class")), ("owner", ("name of owner", "owner etc", "name of", "owner")),
    ("khasra", ("khasra no", "khasra")), ("area", ("area", "bigha", "biswa")),
)
_PARENTAGE = re.compile(r"\b(?:s/o|w/o|d/o|m/o)\s+(.+)", re.I)
_OWNER = re.compile(r"(?:^|\s)([A-Za-z][A-Za-z .'-]{2,}?)\s+\b(?:s/o|w/o|d/o|m/o)\b", re.I)
_KHASRA = re.compile(r"\b\d{1,3}\s*/\s*/\s*\d{1,3}(?:\s*/\s*\d{1,3})?(?:\s+min)?\b", re.I)
_AREA = re.compile(r"\b\d+\s*[-–—]\s*\d+(?:\s*[-–—]\s*\d+)?\b")
_DITTO = re.compile(r"^\s*(?:-\s*do\s*-|ditto|do\.?|same\s+as\s+above)\s*$", re.I)

def detect_column_schema(page: int, tokens: Iterable[NmToken], page_width: float) -> NmColumnSchema | None:
    anchors: dict[str, float] = {}
    for token in tokens:
        text = token.text.lower()
        for role, labels in _LABELS:
            if any(label in text for label in labels): anchors.setdefault(role, token.x + token.width / 2); break
    if len(anchors) < 3 or page_width <= 0: return None
    ordered = sorted(anchors.items(), key=lambda item: item[1])
    return NmColumnSchema(page, tuple((role, 0 if i == 0 else (ordered[i-1][1] + centre) / 2, page_width if i == len(ordered)-1 else (centre + ordered[i+1][1]) / 2) for i, (role, centre) in enumerate(ordered)), "PrintedHeaderAnchors")

def semantic_owner_blocks(tokens: Iterable[NmToken], schema: NmColumnSchema | None) -> list[NmOwnerBlock]:
    if schema is None: return []
    blocks: list[NmOwnerBlock] = []; current: NmOwnerBlock | None = None; prior: NmOwnerBlock | None = None
    for token in sorted(tokens, key=lambda item: (item.y, item.x)):
        column = schema.column_for(token); text = " ".join(token.text.split())
        if not text or column is None: continue
        if column == "owner":
            owner = _OWNER.search(text)
            if owner:
                current = NmOwnerBlock(token.page, len(blocks) + 1, recorded_name=owner.group(1).strip(), name_token=token, source_tokens=[token]); parent = _PARENTAGE.search(text)
                if parent: current.parentage, current.parentage_token = parent.group(1).strip(), token
                blocks.append(current); continue
        if current is None: continue
        current.source_tokens.append(token)
        if column == "owner":
            parent = _PARENTAGE.search(text)
            if parent and not current.parentage: current.parentage, current.parentage_token = parent.group(1).strip(), token
            elif text.lower().startswith("r/o"): current.residence, current.residence_token = text[3:].strip(), token
            elif re.fullmatch(r"\d+\s*/\s*\d+", text): current.share_raw, current.share_token = text, token
        elif column == "khasra":
            if _DITTO.match(text):
                if prior and prior.parcels and all(p.khasra_token and p.area_token for p in prior.parcels):
                    current.ditto_token, current.inherited_from_sequence = token, prior.sequence
                    current.parcels.extend(NmParcel(p.raw_khasra, p.raw_area, p.land_class, p.khasra_token, p.area_token, p.land_class_token, True) for p in prior.parcels)
                else: current.exceptions.append("AmbiguousDittoScope")
            else:
                values = _KHASRA.findall(text)
                if not values: current.exceptions.append("IncompleteKhasra")
                else: current.parcels.extend(NmParcel(raw_khasra=value, khasra_token=token) for value in values); prior = current
        elif column == "area":
            values = _AREA.findall(text)
            if len(values) == 1 and current.parcels and not current.parcels[-1].inherited: current.parcels[-1].raw_area, current.parcels[-1].area_token = values[0], token
            elif values: current.exceptions.append("ParcelAreaMismatch")
        elif column == "land_class" and current.parcels and not current.parcels[-1].inherited: current.parcels[-1].land_class, current.parcels[-1].land_class_token = text, token
        elif column in {"land_compensation", "structure_compensation", "base_total", "solatium", "additional_compensation", "interest", "grand_total"}: current.components[column] = (text, token)
    for block in blocks:
        if not block.parcels: block.exceptions.append("IncompleteKhasra")
        if any(not parcel.inherited and parcel.raw_area is None for parcel in block.parcels): block.exceptions.append("ParcelAreaMismatch")
        if not block.share_raw: block.exceptions.append("MissingRequiredSourceEvidence")
    return blocks
