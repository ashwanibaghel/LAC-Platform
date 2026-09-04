# Award PDF assisted extraction

Award PDF processing is a staging-only workflow. Uploading a PDF stores its metadata and file outside PostgreSQL, creates a durable extraction job, persists per-page evidence, then creates an `AwardIngestionSession` and typed candidates. It never creates or updates canonical Award, Khasra, Notification, Claim, Party, Court Case, or possession records until a user reviews and commits selected Ready candidates.

The job runs in the local application background service, so a browser refresh does not cancel it. Progress is available from `GET /api/award-pdf-extractions/{id}`. Each extracted page records text, normalized token coordinates, method, warnings, and source references used by the review queue.

## Local-only extraction

The current implementation uses PdfPig for embedded-text PDF pages and provides an `IOcrEngine` seam for local OCR. The default engine deliberately reports scanned pages as needing review when no local OCR engine has been configured; it never sends document content to an external OCR or AI service. A future Windows deployment can register a locally installed Tesseract-compatible `IOcrEngine` without changing business parsers or the canonical domain.

PdfPig is used under its permissive MIT license. QuestPDF is used only by the fictional-PDF test fixture under its Community license. No PDF is stored in PostgreSQL, and the local document store creates safe server-generated names.

## Safety and review

- Use artificial documents only in development.
- Review candidates before commit; preview/extraction has no canonical writes.
- Award identity discovered without an existing target Award remains a review candidate.
- A Khasra candidate uses the existing qualifier-aware matching pipeline and does not overwrite Village master area with Award table information.
- Pages with no usable embedded text become `NeedsReview` evidence unless local OCR is configured.
