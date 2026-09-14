"""Deterministic, evidence-first semantics for NM registers.

Every observation retains its own source token. This module has no database
dependency and never fills a source value from the Village master.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from decimal import Decimal, InvalidOperation
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
        role = next((role for role, left, right in self.bands if left <= centre < right), None)
        if role in _COMPENSATION_COLUMNS:
            # A bbox may graze a printed rule. Give it to the centre column
            # only when that column owns strictly more of the bbox than every
            # neighboring compensation band; ties remain unresolved.
            overlaps = {
                band_role: max(0.0, min(token.right, right) - max(token.x, left))
                for band_role, left, right in self.bands if band_role in _COMPENSATION_COLUMNS
            }
            if not overlaps or overlaps.get(role, 0) <= max((value for band_role, value in overlaps.items() if band_role != role), default=0):
                return None
        return role

@dataclass
class NmParcel:
    raw_khasra: str | None = None; raw_area: str | None = None; land_class: str | None = None
    khasra_token: NmToken | None = None; area_token: NmToken | None = None; land_class_token: NmToken | None = None
    area_extraction_method: str | None = None; area_confidence: float | None = None
    inherited: bool = False
    inherited_from_sequence: int | None = None; ditto_token: NmToken | None = None

@dataclass(frozen=True)
class MoneyCellCandidate:
    raw_amount: str
    tokens: tuple[NmToken, ...]
    column: str
    def __iter__(self):
        # Retain the tuple protocol used by existing semantic consumers while
        # exposing every contributing source token for staging provenance.
        yield self.raw_amount
        yield self.tokens[0]
    def __getitem__(self, index: int):
        return (self.raw_amount, self.tokens[0])[index]

@dataclass
class NmOwnerBlock:
    page: int; sequence: int; recorded_name: str | None = None; parentage: str | None = None
    residence: str | None = None; share_raw: str | None = None
    name_token: NmToken | None = None; parentage_token: NmToken | None = None; residence_token: NmToken | None = None; share_token: NmToken | None = None
    parcels: list[NmParcel] = field(default_factory=list)
    components: dict[str, MoneyCellCandidate] = field(default_factory=dict)
    exceptions: list[str] = field(default_factory=list)
    ditto_token: NmToken | None = None; inherited_from_sequence: int | None = None
    parcel_count_as_recorded: str | None = None; total_area_as_recorded: str | None = None
    source_tokens: list[NmToken] = field(default_factory=list)
    @property
    def auto_structured(self) -> bool:
        return bool(self.recorded_name and self.name_token and self.parcels and all(p.khasra_token and p.area_token for p in self.parcels) and not self.exceptions)

# Specific labels precede their broad suffixes: grand total must not be
# consumed as total and structure compensation must not become land compensation.
_LABELS = (
    ("grand_total", ("grand total", "final total")), ("additional_compensation", ("additional compensation", "23(1a)", "@ 12%")),
    ("structure_compensation", ("structure compensation",)), ("land_compensation", ("land compensation",)),
    ("base_total", ("base total", "total (7", "total")), ("solatium", ("solatium", "@ 30%")), ("interest", ("interest", "u/s 34", "@ 9%")),
    ("land_class", ("land class", "class")), ("owner", ("name of owner", "owner etc", "name of", "owner")),
    ("khasra", ("khasra no", "khasra")), ("area", ("area", "bigha", "biswa")),
)
_RELATIONSHIP_MARKER = r"(?:[swdm]\s*/?\s*o)"
_PARENTAGE = re.compile(rf"\b{_RELATIONSHIP_MARKER}\s+(.+)", re.I)
_OWNER = re.compile(rf"(?:^|\s)([A-Za-z][A-Za-z .'-]{{2,}}?)\s+\b{_RELATIONSHIP_MARKER}\b", re.I)
# The register sometimes prints the rectangle separator as a single slash in
# OCR (for example, ``26/21/3``).  Three explicit numeric parts are enough to
# restore just that separator later; two-part values remain incomplete.
_KHASRA = re.compile(r"\b(?:\d{1,3}\s*/\s*/\s*\d{1,3}(?:\s*/\s*\d{1,3})?|\d{1,3}\s*/\s*\d{1,3}\s*/\s*\d{1,3})(?:\s+min)?\b", re.I)
_AREA = re.compile(r"\b\d+\s*[-–—]+\s*\d+(?:\s*[-–—]+\s*\d+)?\b")
# A two-part slash value may be a source-present but incomplete Khasra OCR
# observation (for example, ``31/11``).  It is kept raw only when an Area on
# the same printed baseline independently proves that it is a parcel row; it
# is never normalized into additional numeric parts.
_PARCEL_SOURCE = re.compile(r"\b\d{1,3}\s*/\s*\d{1,3}(?:\s*/\s*\d{1,3})*\b", re.I)
_DITTO = re.compile(r"^\s*(?:-\s*(?:do|tho)\s*-|ditto|do\.?|same\s+as\s+above)\s*$", re.I)
_KITA = re.compile(r"\bkita\b", re.I)
_SHARE = re.compile(r"\bshare\s*[:\-]?\s*(\d{1,3}\s*/\s*\d{1,3})\b", re.I)
_BARE_SHARE = re.compile(r"^\s*(\d{1,3}\s*/\s*\d{1,3})\s*$")
_MONEY = re.compile(r"^(?:rs\.?|₹)?\s*\d{1,3}(?:,\d{2,3})*(?:\.\d{2})?$|^\d+(?:\.\d{2})?$", re.I)
_LAND_CLASS = re.compile(r"^[A-Za-z]{1,3}$")

_COMPENSATION_COLUMNS = {"land_compensation", "structure_compensation", "base_total", "solatium", "additional_compensation", "interest", "grand_total"}

def _header_role(text: str) -> str | None:
    """Classify a printed compensation header without using page coordinates.

    OCR may insert harmless words (for example ``etc``) between the printed
    words of a header, or read ``@`` as ``a``.  This is intentionally limited
    to header-region phrase candidates; body values never pass through it.
    """
    compact = " ".join(text.lower().replace("@", "a").split())
    if re.search(r"\bstructure\b.*\bcompensation\b", compact): return "structure_compensation"
    if re.search(r"\bland\b.*\bcompensation\b", compact): return "land_compensation"
    if "solatium" in compact: return "solatium"
    if "compensation" in compact and re.search(r"\b(?:a\s*)?12\s*%", compact): return "additional_compensation"
    if "interest" in compact: return "interest"
    if "final total" in compact: return "grand_total"
    if "total" in compact: return "base_total"
    return None

def _money_value(text: str) -> str | None:
    """Accept a printed money cell, never header rates or Running Total."""
    compact = text.replace(" ", "")
    if "runningtotal" in compact.lower(): return None
    if not _MONEY.fullmatch(compact): return None
    candidate = compact
    # A bare percentage/header numeral (for example, 30 in '@30%') is not an
    # owner amount. Printed amounts retain a currency prefix, comma, or cents.
    if not any(marker in candidate.lower() for marker in ("rs", "₹", ",", ".")): return None
    return candidate

def normalize_money_amount(raw: str | None) -> Decimal | None:
    """Return a decimal only for an already complete, printed money cell.

    This is deliberately a formatting-only normalization: it removes the
    recognized currency marker and grouping commas, then lets ``Decimal``
    preserve the source digits and cents exactly.  It never repairs OCR.
    """
    if raw is None:
        return None
    complete = _money_value(raw)
    if complete is None:
        return None
    numeric = re.sub(r"^(?:rs\.?|₹)", "", complete, flags=re.I).replace(",", "")
    try:
        return Decimal(numeric)
    except InvalidOperation:
        return None

def _money_cells(tokens: list[NmToken], column: str) -> list[MoneyCellCandidate]:
    """Build only source-complete money cells from one printed row/column."""
    cells: list[MoneyCellCandidate] = []
    ordered = sorted(tokens, key=lambda token: token.x)
    index = 0
    while index < len(ordered):
        pieces = [ordered[index]]; cursor = index + 1
        # A continuation must be a near, same-baseline OCR piece. Its text is
        # concatenated verbatim: punctuation and digits are never repaired.
        while cursor < len(ordered):
            previous, following = pieces[-1], ordered[cursor]
            gap = following.x - previous.right
            if abs((following.y + following.height / 2) - (previous.y + previous.height / 2)) > 18 or gap < -2 or gap > 35: break
            pieces.append(following); cursor += 1
        raw = "".join(token.text.replace(" ", "") for token in pieces)
        value = _money_value(raw)
        if value is not None:
            cells.append(MoneyCellCandidate(value, tuple(pieces), column))
        # A valid-looking prefix with an adjacent source continuation is not
        # emitted independently: the whole source cell must validate.
        index = cursor if len(pieces) > 1 else index + 1
    return cells

def detect_column_schema(page: int, tokens: Iterable[NmToken], page_width: float) -> NmColumnSchema | None:
    source = list(tokens)
    anchors: dict[str, float] = {}
    # Header OCR frequently splits printed labels ("Land" + "Compensation")
    # into adjacent words. Build short same-line phrases before matching role
    # labels, but only above the first owner row so body text cannot create
    # synthetic column anchors.
    owner_y = min((token.y for token in source if _OWNER.search(token.text)), default=float("inf"))
    header = [token for token in source if token.y <= owner_y]
    candidates = list(header)
    for left in header:
        for right in header:
            if right is left or right.x <= left.x: continue
            if abs((left.y + left.height / 2) - (right.y + right.height / 2)) > 45: continue
            if right.x - left.right > 180: continue
            candidates.append(NmToken(page, f"{left.text} {right.text}", left.x, min(left.y, right.y), right.right - left.x, max(left.height, right.height)))
    for token in candidates:
        text = token.text.lower().replace("@ ", "@")
        compensation_role = _header_role(text)
        if compensation_role is not None:
            centre = token.x + token.width / 2
            if compensation_role == "base_total" and "base_total" in anchors:
                if centre > anchors["base_total"]: anchors.setdefault("grand_total", centre)
            else:
                anchors.setdefault(compensation_role, centre)
            continue
        for role, labels in _LABELS:
            if any(label.replace("@ ", "@") in text for label in labels):
                centre = token.x + token.width / 2
                if role == "base_total" and "total" in text and "base_total" in anchors:
                    if centre > anchors["base_total"]: anchors.setdefault("grand_total", centre)
                else:
                    anchors.setdefault(role, centre)
                break
    if len(anchors) < 3 or page_width <= 0: return None
    ordered = sorted(anchors.items(), key=lambda item: item[1])
    return NmColumnSchema(page, tuple((role, 0 if i == 0 else (ordered[i-1][1] + centre) / 2, page_width if i == len(ordered)-1 else (centre + ordered[i+1][1]) / 2) for i, (role, centre) in enumerate(ordered)), "PrintedHeaderAnchors")

def semantic_owner_blocks(tokens: Iterable[NmToken], schema: NmColumnSchema | None) -> list[NmOwnerBlock]:
    if schema is None: return []
    tokens = list(tokens)
    blocks: list[NmOwnerBlock] = []; current: NmOwnerBlock | None = None; prior: NmOwnerBlock | None = None
    awaiting_kita_count = False; awaiting_kita_area = False
    def baseline(token: NmToken) -> float: return token.y + token.height / 2
    def same_row(left: NmToken, right: NmToken) -> bool: return abs(baseline(left) - baseline(right)) <= 22
    def has_row_area(token: NmToken) -> bool:
        return any(schema.column_for(candidate) == "area" and same_row(token, candidate) and len(_AREA.findall(candidate.text)) == 1 for candidate in tokens)
    def attach_area(block: NmOwnerBlock, token: NmToken, value: str) -> bool:
        candidates = [parcel for parcel in block.parcels if not parcel.inherited and parcel.khasra_token and parcel.raw_area is None and same_row(parcel.khasra_token, token)]
        if len(candidates) != 1: return False
        parcel = candidates[0]
        parcel.raw_area, parcel.area_token, parcel.area_extraction_method, parcel.area_confidence = value, token, "FullPageOcr", token.confidence
        return True
    def attach_land_class(block: NmOwnerBlock, token: NmToken, value: str) -> bool:
        candidates = [parcel for parcel in block.parcels if not parcel.inherited and parcel.khasra_token and parcel.land_class is None and same_row(parcel.khasra_token, token)]
        if len(candidates) != 1: return False
        candidates[0].land_class, candidates[0].land_class_token = value.upper(), token
        return True
    for token in sorted(tokens, key=lambda item: (item.y, item.x)):
        column = schema.column_for(token); text = " ".join(token.text.split())
        if not text or column is None: continue
        if column == "owner":
            owner = _OWNER.search(text)
            if owner:
                # Ditto is allowed to see only the block directly above this
                # owner in the same source flow. Do not retain an older owner
                # merely because the intervening block had no usable parcels.
                prior = current
                current = NmOwnerBlock(token.page, len(blocks) + 1, recorded_name=owner.group(1).strip(), name_token=token, source_tokens=[token]); parent = _PARENTAGE.search(text)
                if parent: current.parentage, current.parentage_token = parent.group(1).strip(), token
                blocks.append(current); continue
        if current is None: continue
        current.source_tokens.append(token)
        # ``Kita <count> <total area>`` is a source summary, not another
        # parcel.  It can straddle the owner, khasra, and area bands, so bind
        # it by its sequential printed grammar rather than a master lookup.
        if _KITA.search(text):
            count = re.search(r"\bkita\s+(\d+)\b", text, re.I)
            if count: current.parcel_count_as_recorded = count.group(1); awaiting_kita_area = True
            else: awaiting_kita_count = True
            continue
        if awaiting_kita_count and re.fullmatch(r"\d+", text):
            current.parcel_count_as_recorded = text; awaiting_kita_count = False; awaiting_kita_area = True
            continue
        if awaiting_kita_area and column == "area":
            values = _AREA.findall(text)
            if len(values) == 1: current.total_area_as_recorded = values[0]
            else: current.exceptions.append("ParcelAreaMismatch")
            awaiting_kita_area = False
            continue
        if column == "owner":
            # Printed Khasra cells begin left of the header-derived band on
            # these registers. A complete Khasra grammar is unambiguous in
            # that overlap; a two-part fraction such as a share is not.
            share = _SHARE.search(text)
            if share:
                current.share_raw, current.share_token = share.group(1), token
                continue
            bare_share = _BARE_SHARE.fullmatch(text)
            if bare_share and not current.share_raw and not current.parcels:
                current.share_raw, current.share_token = bare_share.group(1), token
                continue
            values = _KHASRA.findall(text)
            if values:
                current.parcels.extend(NmParcel(raw_khasra=value, khasra_token=token) for value in values)
                continue
            source_values = _PARCEL_SOURCE.findall(text)
            if source_values and has_row_area(token):
                current.parcels.extend(NmParcel(raw_khasra=value, khasra_token=token) for value in source_values)
                continue
            parent = _PARENTAGE.search(text)
            if parent and not current.parentage: current.parentage, current.parentage_token = parent.group(1).strip(), token
            elif text.lower().startswith("r/o"): current.residence, current.residence_token = text[3:].strip(), token
            else: pass
        elif column in {"khasra", "area", "land_class"} and _DITTO.match(text):
            # Ditto is meaningful only in a printed parcel band. It can repeat
            # land fields from the immediately preceding same-page owner, never
            # identity, share, summaries, or compensation.
            safe_prior = prior and prior.page == current.page and prior.parcels and all(p.khasra_token and p.area_token and not p.inherited for p in prior.parcels)
            if safe_prior and not current.parcels:
                current.ditto_token, current.inherited_from_sequence = token, prior.sequence
                current.parcels.extend(NmParcel(p.raw_khasra, p.raw_area, p.land_class, p.khasra_token, p.area_token, p.land_class_token, inherited=True, inherited_from_sequence=prior.sequence, ditto_token=token) for p in prior.parcels)
            else: current.exceptions.append("AmbiguousDittoScope")
        elif column == "khasra":
            values = _KHASRA.findall(text)
            if values:
                current.parcels.extend(NmParcel(raw_khasra=value, khasra_token=token) for value in values)
            else:
                source_values = _PARCEL_SOURCE.findall(text)
                if source_values and has_row_area(token): current.parcels.extend(NmParcel(raw_khasra=value, khasra_token=token) for value in source_values)
                else: current.exceptions.append("IncompleteKhasra")
        elif column == "area":
            values = _AREA.findall(text)
            if len(values) == 1 and attach_area(current, token, values[0]):
                pass
            elif values: current.exceptions.append("ParcelAreaMismatch")
        elif column == "land_class" and current.parcels:
            if _LAND_CLASS.fullmatch(text) and attach_land_class(current, token, text):
                pass
            else: current.exceptions.append("LandClassUnresolved")
        elif column in _COMPENSATION_COLUMNS:
            money = _money_value(text)
            if money: current.components[column] = MoneyCellCandidate(money, (token,), column)
            else: current.exceptions.append("CompensationUnresolved")
    # Compensation cells belong to the owner whose vertical source span contains
    # their baseline. Rebuild component bindings from those spans rather than
    # carrying the mutable "current" owner through subsequent rows.
    starts = sorted(blocks, key=lambda block: block.name_token.y if block.name_token else 0)
    page_bottom = max((token.y + token.height for token in tokens), default=0)
    for index, block in enumerate(starts):
        if block.name_token is None: continue
        start_y = block.name_token.y
        end_y = starts[index + 1].name_token.y if index + 1 < len(starts) and starts[index + 1].name_token else page_bottom + 1
        block.components = {}
        # Same-baseline candidate rows are intentionally evaluated only inside
        # the span. Column bands, not token order, determine component role.
        summary_anchors = [token.y + token.height / 2 for token in tokens if start_y <= token.y + token.height / 2 < end_y and (_KITA.search(token.text) or "total area" in token.text.lower())]
        # Synthetic/unit fixtures may omit the printed Kita label; in that
        # case use the densest valid money row. Real pages still anchor on
        # the printed summary grammar, rather than an arbitrary distance from
        # Kita, so modest baseline skew remains part of one summary cluster.
        summary_y = min(summary_anchors, key=lambda value: abs(value - start_y)) if summary_anchors else None
        running_baselines = [token.y + token.height / 2 for token in tokens if start_y <= token.y + token.height / 2 < end_y and "running total" in token.text.lower()]
        money_tokens = []
        for token in tokens:
            baseline = token.y + token.height / 2
            column = schema.column_for(token)
            if not (start_y <= baseline < end_y and column in _COMPENSATION_COLUMNS): continue
            if any(abs(baseline - running_baseline) <= 18 for running_baseline in running_baselines): continue
            # Keep malformed money-like pieces in the row cluster so a valid
            # prefix cannot bypass an adjacent incomplete continuation.
            if re.search(r"(?:rs\.?|₹|\d)", token.text, re.I) and "running total" not in token.text.lower():
                money_tokens.append((baseline, column, token))
        # Keep only the closest money baseline cluster to the Kita/total-area
        # anchor; a later Running Total row must never become Final/Interest.
        clusters: list[list[tuple[float, str, NmToken]]] = []
        for item in sorted(money_tokens, key=lambda value: value[0]):
            cluster = next((group for group in clusters if abs(group[-1][0] - item[0]) <= 18), None)
            if cluster is None: clusters.append([item])
            else: cluster.append(item)
        if clusters:
            selected = max(clusters, key=lambda group: len(group)) if summary_y is None else min(clusters, key=lambda group: (abs(sum(item[0] for item in group) / len(group) - summary_y), -len(group)))
            for column in _COMPENSATION_COLUMNS:
                row_tokens = [token for _, role, token in selected if role == column]
                cells = _money_cells(row_tokens, column)
                if len(cells) == 1: block.components[column] = cells[0]
    for block in blocks:
        if not block.parcels: block.exceptions.append("IncompleteKhasra")
        if any(not parcel.inherited and parcel.raw_area is None for parcel in block.parcels): block.exceptions.append("ParcelAreaMismatch")
        if not block.share_raw: block.exceptions.append("MissingRequiredSourceEvidence")
    return blocks
