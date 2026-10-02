# Bounded hardware preflight v1: mount failure, not a model result

Private kernel `ashwanibaghel9027/lac-v3-t4-curriculum-preflight`, version 1,
failed at 0.77 seconds before dependency installation, model loading or training.
The recovered log states `Exactly one public-only preflight archive required`.
The Kaggle dataset file inventory proves the service expanded the uploaded ZIP
into its 18 manifest-listed files, including `schema/contracts.py`.

The runner now supports either one ZIP or one expanded manifest inventory.
It copies only listed allowlisted file types, rejects escaping paths, and still
verifies every bundled SHA before installing dependencies or loading the model.
This does not change gold, parsers, context, architecture or six-step budget.
Kaggle software tests: 40 passed, including expanded-mount and path-escape tests.
There is no GPU/memory/quality success claim from version 1.

Original local upload archive SHA:
`1790529d766b87f1db1086290fb593e5a254c9925d34e6f9e14368676e4dafdb`.
Recovered log: `D:/LAC-Training-Artifacts/v3-t4-preflight-error-v1/lac-v3-t4-curriculum-preflight.log`.
