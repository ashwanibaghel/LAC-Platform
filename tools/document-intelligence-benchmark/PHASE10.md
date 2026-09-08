# Phase 10 multi-view agreement calibration

Phase 10 evaluates four fixed, local RapidOCR views: canonical unified crop,
larger-padding crop, 2x crop, and contrast/upscale crop. They are views of one
recognizer, so the result is called **MultiViewAgreement**, not independent
engine consensus.

The calibration policy is deliberately precision-first. Khasra master lookup
happens only after a strong OCR agreement and strict grammar validation; it
confirms an exact value but never substitutes a different master value. Areas
have no master correction path. Field-level HighConfidence does not make a row
SafeExact; every critical field plus proven geometry and an explicit rectangle
are required.

The experimental gate is frozen only when it has zero observed errors on both
the independent held-out Gold-75 and the separately reported HumanLabel-50
pools. Otherwise all results remain review assistance only and no full-document
high-confidence coverage is claimed.
