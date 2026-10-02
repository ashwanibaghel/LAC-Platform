# Pilot V1 bounded native-source dataset

15 matters, 22 actual official orders, 224 independently reviewed examples.
Register inventory: 12 Pending / 3 Disposed; not a current official status claim.
All native pages were read before model evaluation/split freeze. No inference
output was imported. Codex review is not human legal certification.

Frozen assignment: 11 train matters; validation 6384/2024 and 11284/2026;
blind 6203/2026 and 8404/2024. The old 14604/2025, 8664/2021 and 940/2015
development matters are excluded. Same matter/SHA, explicit related source
families and exact/near-duplicate selected passages cannot cross splits.
Future insertion of a connected precedent requires a new leakage audit.

Coverage is **not complete court history**. Sources consist of register-supplied
official public PDFs plus explicitly cited prior actual orders obtained through
the official date-download endpoint. Public index searches yielded no indexed
results for the three multi-order Pending cases. No protected search or CAPTCHA
was automated. The cited 11 August 2025 direct download for 10308/2024 returned
non-PDF content and was excluded, not reconstructed as an independent order.
Prospective listing dates were never downloaded as invented actual orders.

Four supplied chains: 6384 (3 orders), 6203 (3), 11284 (2), 10308 (3).
Only 10308 is a training chain; chronology supervision remains sparse.
Short adjournment orders provide truthful negative examples, not substantive
compensation coverage. The pilot is below the desired dozens-of-orders target;
its useful scope and eventual evaluation must not be overstated.

Two source passages quarantined; one invalid interpretation rejected.
Source numbering/conditions/quoted scope remain verbatim. No private workbook,
raw whole-page text, personal contact details or PDFs enter this public dataset.
Download hashes refer to exact audited bytes. DHC download footers can change
binary SHA on a later download: never relabel a fresh binary as this version.

`export_pilot.py` verifies every selected passage on its exact acquired native
page and uses the unchanged T1 schemas, privacy gate, reviewed-target equality
and conservative leakage rules. It writes only this separate Pilot directory,
not the frozen T1 generated development set.
