# Phase 8 minimal real-label experiment

The local labeller persists every action and resumes safely. A printed Award area such as `4 -- 16` is stored with its visible transcription and a semantic training form `4-16`; this is punctuation normalization only and never a digit or master-data repair.

The B-50 experiment uses the compact pretrained docTR `crnn_mobilenet_v3_large` recognizer (Apache-2.0), locally fine-tuned with human labels weighted above pseudo/synthetic samples. The training guard fails when a manifest includes a held-out gold page/row/column coordinate.

If B-50 has no meaningful held-out improvement, do not collect B-100. The next architecture investigation should focus on recognizers trained for low-resolution tabular scans with explicit CTC character alignment, before requesting additional human work.
