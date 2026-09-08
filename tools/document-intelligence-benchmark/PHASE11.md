# Phase 11 local review accelerator

`review_assist_v11` is deliberately separate from Award ingestion and canonical
land facts. It stores only local experimental review decisions and training
examples, each with a source crop reference and table coordinates. A suggestion,
including an exact master match, never becomes truth until a human accepts or
corrects it. Skip and uncertain decisions never become training gold.

Run it locally after building a review queue:

```powershell
python -m benchmark.review_assist_v11 --queue real-output\assisted-label-v10\label-queue.json --state real-output\review-assist-v11\state.json --reviewer "Reviewer name"
```

The UI is keyboard-first, persists after every action, and intentionally has no
canonical database write or auto-commit action.
