# Phase 7 local real-cell bootstrap

This is an experiment only. It does not connect to the application, write a database, or alter production SafeExact.

`bootstrap_pseudo_labels_v7.py` accepts a real Khasra crop only when the direct OCR text is grammar-valid and exactly equals an exported master value. The master is a confirmation gate; it never edits OCR text. Gold-test coordinates are excluded before selection.

`build_label_queue_v7.py` writes an ignored, varied queue of difficult cells. To label locally after reviewing the visible crop, run:

```powershell
python -m benchmark.assisted_label_v7 --queue real-output/assisted-label-v7/label-queue.json --output real-output/assisted-label-v7/manual-labels.json
```

Enter saves the typed visible text and advances; Escape skips. It creates only ignored local JSON and crop files.

Future design, not implemented in the product: an extraction correction could preserve the evidence crop and a human-confirmed transcription in a local training store; a periodic, separately approved local retraining run would consume that store. Canonical records remain independent of training labels.
