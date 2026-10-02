# Known order index and lazy-source checkpoint

Production reuses `CourtExternalOrderObservation`: CourtCaseId, exact forum/type/
number/year identity, OrderDate, OfficialUrl, CorrigendumUrl, UploadDate and observation
ID. Authorized question handling reads these rows with AsNoTracking; it neither
inserts observations nor modifies Court records. Raw HTML and its EvidenceSha256
are not forwarded as PDF evidence. No duplicate order database or migration exists.

The case-scoped filesystem artifact mirrors this index with processing state,
PDF SHA after fetch, extraction/model/rule versions, timestamp and artifact location.
An explicit question can merge metadata durably without fetching PDFs. GET/render
still reads only stored intelligence. URLs without a verified actual order date,
wrong-forum/identity URLs, unknown URLs and ambiguous same-date publications cannot
trigger lazy processing. PDF identity/date are independently checked again after fetch.

Questions naming one exact known unprocessed order may fetch that single URL,
using the worker's shared OS PDF lock, bounded download/native parsing/local inference,
atomic structured artifact writes and TemporaryDirectory cleanup. Already validated
orders do not re-download per question. Review sources require explicit retry/refresh
wording. Changed known binary hashes fail closed for source-version review; dynamic
DHC footer hashes are not silently treated as equivalent. No PDFs are retained.

Examples: `29 July wali hearing me kya hua?`, `16 April ko affidavit ka kya hua?`.
Missing years resolve only if that day/month identifies exactly one known actual
order; otherwise no other date's evidence is substituted. A listed date with no
actual order returns `I could not find an official order for that listed date.`
Full-story questions retrieve originating context and separate party/Court voices
across the chain. Routine vs substantive presentation is content-based; it never
discards older orders or changes canonical status.

Limitations: newly observed URLs reach the index on the next authorized question,
not an automatic crawl. Long lazy extraction may time out calmly and leave review
work; use the explicit sequential worker for large orders. No schema is needed now.
If durable shared multi-host intelligence storage is later needed, design it
separately rather than mutating the office DB during this quality loop.

Acceptance: date-specific local-model answers for 29 July and 16 April 2026
returned source passages from only their requested order. 1 October returned the
missing-order response, not the earlier scheduled-date passage as a new event.
The first full-story answer over-weighted routine bench context; retrieval now
prefers the verified originating dispute over procedural context and has a
regression test. Party voices remain explicitly attributed submissions.

Validation: Python safety suite 114 passed; frontend 86 passed; direct Vite build
passed (existing large-bundle warning). Release .NET build passed; focused API
artifact/metadata tests 3 passed. Full .NET invocation stalled after test-file
discovery; allowed beyond the configured five-minute hang timeout, then stopped
the owned test process after approximately six minutes. No blame dump appeared;
no full-suite pass is claimed. Local inference passes were rerun without that
memory-intensive testhost competing for RAM.
