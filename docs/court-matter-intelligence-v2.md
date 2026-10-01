# Court Matter Intelligence V2 — fact-first rebuild

Base: `7243c66f42964d32d6ab135ba389e6b487dfa9dc`.
Branch: `codex/court-order-intelligence-phase1`. No main merge.

## Semantic boundary

Order facts and office actions are separate. The local extractor supports the
20 requested semantic roles plus backward-compatible V1 roles. Independently
validated propositions carry stable IDs, exact bounded evidence, date, page,
official URL, scope, attribution, actor and available structured field values.
No canonical Court record, database entity/schema or migration changes.

Every usable order has a representative digest of up to six propositions,
regardless of action count. Explicit dispositions have a reserved digest slot.
The case position selects representative distinct voices chronologically, not
just active office tasks. A complete review-marked extraction may supply its
individually validated propositions; uncertain propositions never participate.
Failed/partial extraction supplies no facts, digest, actions or Q&A evidence.

Party attribution is checked independently against source speech/caption context.
Past-tense allegations, counsel submissions and unknown speakers cannot become
Court findings or land facts. An unknown quoted speaker remains Uncertain.
Neutral narrative cannot arbitrarily acquire a petitioner/LAC speaker. Personal
address lists are omitted from digest anchor selection. The existing strict
office-action and completion-linkage engine is unchanged.

## Grounded questions

English, Hindi, Hinglish and Roman Hindi aliases select topic, role, party,
latest-order and year filters. Unrecognized questions may use one bounded local
strict-JSON planner request containing the question only. Invented years,
unknown fields and invalid planner output are rejected. Retrieval stays within
the current matter, excludes uncertain/quoted/cross-case evidence, and remains
bounded to eight facts / 6,500 context characters.

The local model selects at most four retrieved IDs. The server supplies exact
text, attribution, order/date/page and official links; the model cannot invent
evidence or remove negation/qualifiers. Invalid generated selection falls back
to deterministic attributed extracts. No evidence produces the explicit calm
insufficient-evidence response. Model failure leaves the intelligence page and
the rest of LAC Platform available. No cloud inference or render-time action.

## Officer presentation

Context, compact action attention, representative current position, an attributed
"What happened in this order" digest, multilingual Ask and collapsed order history.
No-action orders retain prominent facts. One collapsed verification notice,
not repeated banners. Preferred periods remain qualified and verbatim in evidence.
Paragraph numbering is removed only from officer text, never source evidence.

The isolated public-order demo uses synthetic IDs and a separate extraction root
on port 5176. Its five showcase matters are 8664/2021 (Section 18 direction),
568/2024 (compensation), 17415/2024 (possession/acquisition), 2687/2018 (reference
limitation), and 120/2026 (Section 30/31 reference). Earlier connected/scanned/
unavailable-source matters remain under Additional test matters. These artifacts
must never be copied onto registered Court-case IDs.

## Local acceptance — 2 October 2026

All five showcase sources completed every selected chunk. Review-marked sources
have no extraction failure and expose only individually admitted propositions.

| Matter | Order | Source state | Usable propositions | Digest | Recognized LAC actions |
| --- | --- | --- | ---: | ---: | ---: |
| 8664/2021 | 30 Jan 2025 | NeedsReview | 32 | 6 | 1 |
| 568/2024 | 23 Apr 2026 | NeedsReview | 9 | 6 | 0 |
| 17415/2024 | 18 Dec 2024 | Validated | 14 | 6 | 0 |
| 2687/2018 | 23 Apr 2026 | NeedsReview | 23 | 6 | 0 |
| 120/2026 | 7 Jan 2026 | NeedsReview | 16 | 6 | 0 |

17415/2024 demonstrates useful no-action intelligence: the acquisition-lapse
issue, separately attributed petitioner possession claim, historical certificate,
Court consideration of compensation/possession and dismissal of the writ. The
older usable 4 Aug 2015 order in test matter 7363/2015 also retains notice,
affidavit, listing and status-quo facts without a recognized direct LAC action.
Its unavailable newer source remains visibly review-marked, not suppressed.

Zero recognized actions in the table is not a legal conclusion that LAC has no
duty: notably 568/2024 retains the conditional "amounts due ... if any" direction
to release by LAC, while the unchanged narrow subject grammar does not create an
office task for that passive wording. That limitation is explicit, not masked.

Real local-model browser questions passed: Hinglish order-summary / LAC-action
queries on 8664/2021, and Hindi Court-position query on 17415/2024. Citations show
the actual order date/page and exact official passage. Browser inspection at
1366×768; no mocked screenshot payloads. Temporary downloaded PDFs were cleaned.

Verification: full intelligence Python 79/79; focused .NET CourtIntelligence
2/2; all frontend 82/82; direct Vite production build PASS (existing large-chunk
warning only). .NET used the existing Release build because no C# source changed.
API 5088 and frontend 5175 remained healthy; no migrations, canonical database
mutation, DHC assisted/sync/search/CAPTCHA action or cloud inference.

## Limits

This is a bounded representative brief, not a complete legal account. Answers
remain attributed English source extracts even when the question is Hindi or
Hinglish. Native-text, caption, exact-case and page checks still fail closed.
No generic OCR, connected-case attribution, inferred compliance or guessed dates.
An unreadable latest order withholds current-position claims; earlier usable
facts remain available in history and time-scoped Q&A. The narrow existing action
grammar may omit directions that do not explicitly match its LAC subject form;
their verified source-backed propositions can still appear in the order digest.
Review-marked showcase sources are not claimed fully validated.
