# Court Q&A language, temporal semantics and referents

Base: `acc2ae71516d278482e1b5cea36160d8b91293bd` (accepted officer UI).
This change is limited to backend Q&A and its tests. Artifact formats, extraction,
rulebook/model versions, source verification, Fast Path, refresh and PDF acquisition
are unchanged. React/CSS are unchanged.

## Language contract

`POST /api/court-cases/{id}/intelligence/ask` accepts the existing `question` and
optional GeneralLocal `history`, plus optional `language`:

```json
{"question":"Case simple language me samjhao","language":"Hinglish"}
```

- Omitted language defaults to `Auto`.
- Supported strings: `Auto`, `English`, `Hindi`, `Hinglish`, case-insensitive.
- Null/unsupported values receive HTTP 400 before the local service is called.
- English produces English narration; Hindi produces Devanagari narration;
  Hinglish produces Roman Hindi/Hinglish narration.
- Auto selects Hindi for Devanagari, Hinglish for common Roman Hindi markers,
  otherwise English. Explicit preference wins over question language.
- `language` in the response is the resolved language.
- Original `claims[].text`, `source.evidence`, `evidenceParts`, URL, date and page
  remain verbatim. Localized narration is accompanied by explicitly quoted original
  passages. It does not translate canonical facts. Detail without an approved
  narrative template remains in the exact cited passage.
- Narration uses deterministic templates; the existing model still selects fact
  IDs only. Language adds **zero model calls**, including the existing busy Fast
  Path. GeneralLocal receives the preference in its existing call.
- The internal Python `answer` function retains English as its legacy default;
  the HTTP receiver explicitly passes the request's Auto/default preference.

## Audit of the reported four-week answer

Real matter: `WPC NO. 6203/2026`,
`665ec7ce-c31b-4320-9c74-45b69e14327a`.

Accepted canonical `current.json` SHA-256:
`9a7f7e3d2ee55acc264373d73d45e5d8c03619061e24388f13a95cfbb4859b51`.
All eight accepted orders were read without processing/downloading PDFs.
`beforeNextHearing` is empty. **No fact, evidence, summary, proposition or action
in this artifact contains a four-week compliance-report direction.** The literal
previous answer's origin cannot be conclusively attributed without its saved
response/citations or old runtime. The accepted ID-only answer renderer cannot
generate that sentence from these eight verified artifacts. A four-week status
report appears in synthetic unit-test data; this is not evidence that the real
runtime used that data.

A distinct, reproducible temporal presentation defect was found: fact scope
`Current` refers to the statement in its source order, not to an outstanding task
today. Broad summary retrieval formerly rendered older COURT_DIRECTION facts
without a date/historical preface; lifecycle gating applied mainly to LAC-action
questions. "Case simple language me samjhao" also lacked a deterministic full-case
intent and could depend on classification.

The actual earlier LAC/DJB direction is **6 May 2026, page 5**:

> 14. Accordingly, the LAC and Delhi Jal Board, which is the agency for whom the land has been acquired, shall obtain instructions in this matter on these two aspects and file short affidavits by the next date of hearing.

Official source: https://delhihighcourt.nic.in/app/showlogo/1778239354_dc539e4745899eea_pms_62032026.pdf/2026

This is not a four-week compliance-report direction. The **21 August 2026,
page 6** affidavit passage is explicitly conditional ("if they wish to do so"),
with a 10 September date. The **14 September** record states that LAC/DJB affidavits
were recently filed. These are not promoted into current mandatory LAC work.

Q&A now marks non-outstanding earlier directions as historical with their source
date. Only existing `beforeNextHearing` evidence can establish a current mandatory
LAC action. Source speech is quoted with dated attribution; no expiry/completion
or supersession is invented. Full-case and historical-office answers explicitly
retain the no-current-mandatory-action conclusion when that collection is empty.

## Latest verified order

The latest verified order is **23 September 2026**. Its facts are a counsel
appearance correction, application disposal and a recorded listing event; none
is a verified COURT_DIRECTION. The answer therefore states no fresh direction
is established in that latest order. It separately cites the most recent earlier
verified direction, **18 September 2026, page 1**:

> 3. List on 23rd September, 2026.

Official source: https://delhihighcourt.nic.in/app/showlogo/1789971883_77feb4744dcb6b69_pms_62032026.pdf/2026

Latest-order selection is chronological and uses verified processed sources,
including a successfully processed order with zero admitted facts, independent
of array ordering. Explicit date/year restrictions retain their meaning. No
earlier directive is presented as a directive from the latest order.

## Follow-up context

The accepted frontend sends only GeneralLocal turns. To support grounded
follow-ups without changing it, the authenticated backend keeps ephemeral context
for the current case. Partition: authenticated user ID plus hashed login cookie.
Limits: 256 active session partitions, last four grounded turns, eight source
references per turn, 20-minute idle expiration. API restart clears this context.

Only the user question and source URL/date references from independently validated
claims are retained. Assistant prose, claim text and snippets are never retained
as history evidence or passed as evidence to the model. Every follow-up source is
matched again to currently usable artifact facts before retrieval. Unverified,
stale, missing, cross-case or multiple possible sources require an explicit date.
An unsupported last grounded turn cannot silently fall back to an older referent.

"Latest Court direction" focuses its separately cited earlier directive; thus
"Us order mein kya hua?" and "Ye direction kis date ki thi?" re-retrieve the
18 September order. "Isme LAC ko kya karna tha?" finds no LAC task in that listing
order. The same question with a verified 6 May referent returns the original LAC
direction as historical, alongside the current no-mandatory-action conclusion.

Authorized case reads and asks advance the active case segment. A → B → A clears
old referents; generation checks prevent late old responses repopulating it.
Different users/login sessions cannot share context. Existing GeneralLocal history
and its independent Court-fact gate are preserved.

## Verification evidence

Portable regression fixture contains the eight real orders' original facts and
their original versions/source identities, linked to the accepted artifact hash.
Regressions cover all response languages, unchanged claims, four-week historical
data, latest versus earlier direction, no current mandatory LAC action, grounded
follow-ups, stale/ambiguous/prose-only context, case isolation, and explicit dates.

Read-only real HTTP acceptance and independent ASP.NET citation validation outputs:
`D:\LAC-Court-QA-20261005\real-case-acceptance.json`,
`validation.log`, `canonical-view-after.json`.
Ten responses passed the unchanged `CourtIntelligenceCaseData.ValidateAnswer`.
The canonical view remained **8 official orders, 8 usable briefs, 0 blocked**;
all **11 JSON artifact checksums** remained identical. The acceptance used the
existing busy Fast Path: **zero model calls and zero PDF processing/download calls**.

Final checks:

- Full Court Python discovery: **262 passed**, zero failures/errors.
- Focused Court/DHC .NET suite: **231 passed**, zero failures/skips.
- Full .NET suite: **819 passed**, zero failures/skips.
- Fifteen new Python language/grounding/referent regressions passed.
- Independent .NET validation of the ten real HTTP responses passed.
- `git diff --check` passed; no changes under `src/LAC.Web`.

The initial full .NET run aborted with a test-host stack overflow in the existing
DHC historical sync/EF query path. The complete retry passed using an 8 MiB
**test-process-only** thread stack (`DOTNET_Thread_DefaultStackSize=800000`, plus
the legacy knob). Microsoft documents the hexadecimal environment setting in
[.NET threading runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/threading).
No application configuration was changed. A concurrent Python rerun encountered
host memory pressure; its final separate complete run passed. Test logs are in
`D:\LAC-Court-QA-20261005` (`focused.log`, `all-stack.log`, `python.log`).

The live application was not restarted or deployed. Artifact/runtime roots were
not changed. Generated worktree API binaries were preserved outside C: to make
space for source edits; all new build output is in the task's D: directory.
