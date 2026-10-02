"""Minimize at passage selection; fail closed rather than mutate verbatim evidence.

This is a reject/quarantine gate, not a claim of exhaustive PII detection.
Addresses/IDs require source review too. The private workbook inventory must
never be fed to this training exporter or uploaded to Kaggle.
"""
import re

PATTERNS = {
    "email": r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
    "phone": r"(?<!\d)(?:\+91[ -]?)?[6-9]\d{9}(?!\d)",
    "personal_address": r"\b(?:R/o|S/o|D/o|resident of|residing at|residential address)\b",
    "personal_identifier": r"\b(?:Aadhaar|PAN(?:\s+number)?|passport|bank account)\b"}


def violations(text):
    return [name for name, pattern in PATTERNS.items() if re.search(pattern, text, re.I)]


def require_minimized(example):
    # Official source URLs can contain opaque 10-digit publication timestamps;
    # only text/query content is scanned as personal data, not URL identifiers.
    texts = [p["text"] for p in example["provenance"]]
    texts.append(example["input"].get("question", ""))
    if any(violations(text) for text in texts):
        raise ValueError("Public-data minimization requires passage quarantine/review")
