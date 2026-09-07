# Phase 9 unified cell-crop and pretrained recognizer shootout

All recognition experiments use `cell_crop_pipeline_v9`. It preserves a raw
evidence crop and writes a normalized recognition crop plus deterministic
metadata. Edge-rule suppression is limited to long rules within a 4-pixel crop
edge band; it never performs character or domain-value correction.

The pipeline assigns a source-coordinate identity to every crop. Existing
training leakage checks reject any held-out `(page, row, column)` from the
training manifests. Gold labels and real outputs remain ignored.

The recognition shootout has separate raw and semantic exact measures. Semantic
comparison only permits whitespace and equivalent area dash formatting. It
cannot repair an identifier digit or infer a slash from master data.

`safe_exact_v9` is explicitly an experimental report. It may validate an exact
prediction against the local master, but it cannot change the prediction and
does not enable any production gate.
