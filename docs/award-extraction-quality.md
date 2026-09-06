# Award extraction quality gate

Rule version: `AwardRules/1.0`.

The Award PDF pipeline is local-only and intentionally prioritises identifier precision over recall. It uses two passes: document identity/structure (Award and Village labels, page lines, table headers) followed by supported domain extraction. The rule engine accepts fuzzy matching only for short semantic labels. It never fuzzy-corrects an identifier, date, amount, percentage, area, or Khasra digit.

## Hard gates

An Award Khasra candidate requires all of the following before the ingestion service can mark it ready:

- a detected Khasra table with a Khasra header and at least one area header;
- strict `rectangle//killa[/subdivision] [min]` grammar;
- known Award and Village context; and
- no identifier substitution.

The Village master is only used as exact-match evidence. A detected `22//2/7` remains `22//2/7`; it is never rewritten to a similar master Khasra. Missing Award/Village context is `NeedsReview` (pending context), not an invalid Khasra.

## Local golden corpus

`AwardExtractionRuleEngineTests` contains twelve fictional layout/noise scenarios: born-digital and scanned identity headers, noisy and skewed pages, aliases, Khasra tables including reordered and multi-page layouts, notifications, court/claim narrative, and identifier-like numeric noise. The benchmark utility reports precision, recall and F1 from exact fictional truth values. The malformed values `1627//154`, `02//26100`, and `10106//213` are regression cases and must never become Khasra candidates.

## Review and re-analysis

Every candidate records its rule version, page, gate evidence, warnings and possible exact master match. Review links open the locally stored source PDF at the recorded page. Re-analysis reuses saved page extraction evidence and does not upload the PDF again or write canonical records.

## Current supported automatic structures

`AwardCore`, `AwardVillage`, strict table-bound `AwardKhasra`, and context-bound `Notification` are the current structured rules. Possession, litigation, claims, classifications, valuation, compensation, area issues and supplementary matters remain conservative unmapped findings until dedicated table/context rules are added. They are never inferred from isolated words.
