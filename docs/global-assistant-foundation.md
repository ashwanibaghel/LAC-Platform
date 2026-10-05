# Global assistant conversation foundation

Backend branch: `codex/global-assistant-foundation`.

Accepted base: `ca9546c8b7f3c94f3b7670a9d622777b5b1a5daa` (Court Fast Path).
The commit containing this report is the implementation checkpoint. No frontend
files are changed. Main has not been merged; IIS and the live database have not
been updated.

## Storage and conversation model

Migration `20261004155206_AddAssistantConversations` adds two tables to the
existing local application PostgreSQL database:

| Table | Stored data |
| --- | --- |
| `AssistantConversations` | ID, authenticated owner FK, title, timestamps, active module/entity, context revision, optimistic concurrency revision, visible message count |
| `AssistantMessages` | ID, conversation FK, unique sequence, User/Assistant role, visible text, timestamp, agent/mode/status, module/entity/context revision, public citations/coverage JSON |

User and conversation foreign keys use restricted deletion. There are indexes
for owner/update time and conversation/context/sequence; conversation/sequence
is unique. `Revision` is an EF concurrency token. A user/assistant pair and the
conversation update commit atomically. An inference failure returns a visible,
persisted `Unavailable` reply when storage and authorization remain available.
Cancellation, revoked access and failed storage do not commit a partial pair.

These are conversation entities, not official records, so chat bodies do not
enter the official-record audit pipeline. No hidden reasoning, internal prompts,
generated summaries, cloud memory or model weights are stored. The generated
PostgreSQL migration SQL contains only these two new tables and their indexes/FKs;
EF reports no pending model changes. The migration has **not** been applied to
the live database.

## API contract

All routes require the application's authentication and return `Cache-Control:
no-store`. Ownership is checked using the authenticated local user, never a
client-supplied user ID.

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/assistant/conversations` | 201: conversation metadata and resolved context |
| GET | `/api/assistant/conversations?offset=0&limit=20` | Owned, currently accessible conversations; `hasMore`, `nextOffset` |
| GET | `/api/assistant/conversations/{id}` | Metadata, visible message count and resolved active context |
| GET | `/api/assistant/conversations/{id}/messages?after=0&limit=50` | Ordered visible messages; `hasMore`, `nextAfter` |
| POST | `/api/assistant/conversations/{id}/messages` | Persisted reply, message IDs, agent, mode, status, citations, coverage and context used |

Create:

```json
{
  "title": "Optional title",
  "context": { "module": "Court", "entityId": "<registered CourtCase GUID>" }
}
```

Append:

```json
{
  "message": "Iski deadline kya hai?",
  "context": { "module": "Court", "entityId": "<registered CourtCase GUID>" }
}
```

Omitting/null `context` inherits the active conversation workspace. Explicit
`{"module":"General"}` clears the entity binding and starts a new context
segment. Context contains IDs only; display labels are resolved by the backend.
Only General and Court are initially registered.

Append response fields are `conversationId`, `userMessageId`, `messageId`,
`answer`, `agentUsed`, `mode`, `status`, `citations`, `coverageNote`, `coverage`,
`contextUsed`, `createdAt`, and `conversationRevision`. Court citations retain the
accepted claim/source shape: text, attribution, official URL, order date, page
and exact evidence. Coverage is the accepted Court progress summary. Message
history includes role, sequence, text, timestamp, agent/mode/status, workspace
and metadata containing citations/coverage.

Statuses include `Answered`, `InsufficientEvidence`, `NeedsNarrowing`,
`RequiresContext`, and `Unavailable`; modes are `CourtGrounded`, `GeneralLocal`
or deterministic `AuthenticatedContext`. Partial/background coverage remains
visible through the coverage object and note. Missing verified LAC actions or
deadlines remain insufficient-evidence answers, with no invented tasks.

Bounds: 8 KiB incoming request body (including chunked requests), 600-character
user message, 120-character title, 16,000-character visible assistant message,
128 KiB public metadata, 4,096 messages per thread. Message pages default to 50
and cap at 100; conversation pages default to 20 and cap at 50. Oversized complete
Court history requests return a narrowing instruction rather than silently
truncating chronology.

HTTP errors: 400 invalid request/context/page, 401 unauthenticated/inactive
profile, 403 inaccessible entity, 404 unknown/not-owned conversation, 409 active
turn or stale revision, 413 oversized request, 503 unavailable storage/context.

## Three context layers and registry

`AssistantUserContext` contains only the current active database profile's
display name and designation. Identity questions are intercepted by the
orchestrator before agent selection and answered deterministically. Client
profile overrides, cookie display-name staleness and future registered agents
cannot supply an invented authenticated identity. User IDs, passwords and
permission lists are not sent to inference.

`AssistantWorkspaceContext` supplies bounded module/entity references.
`IAssistantWorkspaceResolver` resolves labels and authorizes an entity using the
existing application authorization service. Court resolution reuses the
registered case/order index. Access is rechecked after inference and before
committing/returning the answer. Metadata/history reads revalidate every stored
workspace; inaccessible threads are omitted from listing, including their titles.

Conversation context contains the last eight messages in the current context
revision, bounded to 600 characters for User and 1,200 for Assistant. Workspace
changes advance the revision, including A → B → A, so old case history cannot
be reused as the new workspace's context. Earlier messages remain stored and
can be paged when their contexts remain accessible.

`AssistantOrchestrator` receives registered `IAssistantAgent` implementations
through DI. Each agent exposes its code, support priority and async answer
method. Highest supported priority wins; a stable code ordering resolves ties.
Explicit general conversation can select General inside a Court workspace;
ambiguous messages in a Court workspace prefer Court. Initial routing is
conservative; adding a resolver/agent does not require database/API changes or
a module switch. A dummy future capability is integration-tested. No Award,
Land, Matter or other future product agents are implemented.

`IAssistantHistoryPolicy` is the future summarization hook. Its initial
implementation only bounds recent turns; it does not create model memory.

## Court adapter and follow-ups

`CourtIntelligenceAssistantAgent` calls the existing accepted
`CourtIntelligenceQuestions.AskAsync` boundary. It passes the current registered
case identity/order index and enables `structuredOnly`, `groundedOnly`, and
`deterministicOnly`. Conversation requests do not call PDF preparation, even
if the question asks for an explicit retry. The existing artifact reader,
source/index filtering, per-claim exact evidence validation, attribution rules,
LAC action lifecycle checks and current-case restrictions remain in force.
Legacy Court endpoints retain their prior default behavior.

Only the last four **User** Court questions are supplied for intent inheritance.
Assistant text and old citations are never transmitted as Court evidence.
`conversation_context.resolve` carries bounded topic, latest/date, party and
direction intent into contextual follow-ups. Explicit new dates/roles override
earlier ones, so a petitioner's prayer does not constrain a subsequent Court
direction question. Chained follow-ups preserve inherited intent. Every answer
retrieves and validates evidence again from the current artifact.

Whole-case, latest-order, chronology, directions, LAC actions, compensation,
possession, references, page citations and source/background state reuse the
accepted engine. Complete chronology is not silently capped at eight claims in
deterministic mode. Text/response limits still require narrowing very large
histories.

Read-only actual eight-order case probe
`665ec7ce-c31b-4320-9c74-45b69e14327a`:

| Question | Retrieval time | Validated claims / result |
| --- | --- | --- |
| Petitioner kya maang raha hai? | 0.301 s | 3 claims |
| Aur Court ne us par kya kaha? | 0.462 s | 4 claims |
| Latest order me LAC ko kya karna hai? | 0.255 s | 0; `LacActionNotEstablished` |
| Iski deadline kya hai? | 0.144 s | 0; `LacActionNotEstablished` |

This probe made zero model calls and zero PDF downloads. Artifact SHA-256
remained `9a7f7e3d2ee55acc264373d73d45e5d8c03619061e24388f13a95cfbb4859b51`.
These are direct local artifact-retrieval timings, not an end-to-end deployed
HTTP/database benchmark.

## General adapter

`GeneralLocalAssistantAgent` calls the dedicated loopback
`/assistant/general` route on the existing local Python/V3 service. It uses
bounded General turns only, with no case labels, artifacts or Court history.
Normal greetings, translation, sentence explanation and ordinary contextual
follow-ups use accepted local inference. The adapter validates mode, empty
claims, bounded text and the absence of Court factual markers; the Python
boundary preserves output, repetition and authenticated-persona guards. There
is no cloud fallback. General Court questions without an authorized Court
workspace return `RequiresContext`. Service outages return `Unavailable`.

The live accepted services remain on their accepted code; the new Python route
and API require a coordinated backend rollout when integrated. This task does
not deploy them or alter V3 weights/configuration.

A direct smoke call through the new General adapter to the restored accepted
local V3 model returned `Hello! How can I assist you today?` with zero claims in
5.34 seconds (172 prompt tokens, 15 generated tokens). The running model command
exactly matches the accepted configuration, its health is `ok`, and temporary
Court PDF buffers are empty. This smoke call did not deploy a new service.

## Verification

- Full .NET suite: **822 passed, 0 failed, 0 skipped** (5m24s).
- Final Assistant/Court integration suite: **34 passed**, including strengthened
  checks for forged identity/context labels and deterministic identity before
  future-agent routing.
- Focused Python conversation/router/grounding checks: **35 passed**.
- Full Python suite ran 256 tests: 249 passed, seven environment errors. Two
  recovery errors were caused by D-drive free space falling below the existing
  1 GiB reserve. Both passed on rerun in a sufficient-space temp directory (all
  four recovery tests passed). The remaining five existing model-startup tests
  timed out launching PowerShell on this host. No tests were skipped, and no
  startup, packaging or evidence guards were weakened.
- EF SQL generated and inspected; no pending model changes. No live migration.
- Frontend diff empty; whitespace check passed. Accepted Court backend base
  preserved. Active UI agents' changes in their own checkout were left untouched.

Integration coverage includes persistent thread append/paging, identity,
ownership, unauthorized context, permission revocation during inference,
history authorization, A/B/A isolation, fresh-evidence validation, conservative
routing, bounded history, concurrent append conflict and extension registration.
Python fixtures additionally cover chained user-intent inheritance, date/party
overrides, assistant-output exclusion, structured-only no-PDF behavior and full
deterministic chronology.

## Frontend integration remaining

1. Add a global assistant surface and conversation selector using the new APIs.
2. Create/reuse conversation IDs, page visible history, and append new replies
   without replacing earlier messages. Display both the user and assistant turn.
3. Send explicit module/entity context when workspace navigation changes; use
   General to clear the binding. Display backend-resolved context labels.
4. Render structured citations, coverage, processing state and insufficient-
   evidence statuses. Handle inaccessible saved history and local outages calmly.
5. Disable duplicate sends while a turn runs. On 409, reload the thread before
   offering retry. There is no client-message idempotency key; do not blindly
   replay a timed-out POST that may already have committed.
6. Apply the reviewed migration and run the API/Python service from the matching
   backend checkpoint in the intended local environment. Streaming/token UI,
   conversation deletion, long-term summarization and multi-agent aggregation
   are separate future work.

No React, CSS, Court visual components or Land/Award UI were edited here.
