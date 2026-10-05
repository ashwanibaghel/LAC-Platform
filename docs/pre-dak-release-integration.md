# Pre-Dak release candidate integration

Branch: `codex/lac-release-candidate-pre-dak`.

This branch combines the accepted Land, Matter, Court and Global Assistant
sources. No unfinished Dak V2 branch was merged or cherry-picked. Existing
accepted Dak foundation and canonical Matter/Dak links remain available.
No deployment was performed, main was not merged, and the office PC/database
was not accessed.

## Accepted sources and graph audit

| Accepted source | Exact SHA | Incorporation |
| --- | --- | --- |
| Land UI | `5770cbaedcdefa8293cac85d662696693abcee2b` | Ancestor of the selected Matter UI base |
| Smart Core Intake backend | `ab504c4e499b2309e4d0b52b959cda29a3bab6be` | Content-equivalent `d3ef1a5411a33485468fbe4d1623a5d71df0e9a7` in that base |
| Acquisition History backend | `03c613f249995c7f950c85ab4fac96f7a524a83d` | Content-equivalent `21b751d164083e297d5bf92bacd6b2b61edab566` in that base |
| Matter backend, including optional primary hardening | `88d7d151adf0458e2053aad9796912a410dfab45` | Ancestor of the selected Matter UI base |
| Final Matter UI | `d9415c684acdb32d687bc9ce8c56738260c92bd4` | Integration starting point |
| Final Court/runtime/deployment source | `7f304418225e9bba2004c75c2ea8c539f58de099` | Merge parent |
| Global Assistant foundation | `1d36af8099a56080a090998720c86f14ff8c7150` | Merge parent |

All relevant refs were fetched before changes. The three source lineages share
`ca9546c8b7f3c94f3b7670a9d622777b5b1a5daa`. Actual file content was compared
for the Smart Intake and Acquisition cherry-picks; commit ancestry was not
used to infer missing functionality. The alternative Court UI correction is
already present with equivalent content in the accepted Court lineage, so it
was not applied again. Accepted source branches were not rewritten.

Court merge commit: `3d41d8eb6342f562e7f928fad3b986565d1f7d1b`.
Assistant merge commit: `7940451b8efad380856c19630bcb026ddc530eb5`.
The final release SHA is reported with the push result rather than embedded
in its own commit.

## Conflict resolutions and bounded release corrections

The Court merge auto-merged startup registrations without conflicts. The
Assistant merge had six conflicts:

- `Program.cs`: retained loopback Court runtime clients and added Assistant
  registration/endpoints alongside the Land and Matter registrations.
- `LacDbContext.cs`: retained Core Intake, Matter relationships and configuration;
  added Assistant conversations/messages and their configuration.
- `CourtIntelligenceQuestions.cs`: combined language and verified source context
  with Assistant's structured-only, grounded-only, deterministic-only and bounded
  recent user-question parameters. Existing response/source validation remains.
- `chat_router.py`: combined selected-language handling with the conservative
  standalone General Assistant route and retained Court-content exclusion.
- `questions.py`: preserved Court's verified-source referent resolution, language
  rendering, zero-evidence status, latest-direction semantics and unavailable-model
  behavior. Global conversation context contributes bounded user intent only;
  it never contributes evidence. Explicit Court source context takes precedence
  when both context forms are supplied.
- `serve_questions.py`: retained status/refresh/health endpoints and added the
  standalone Assistant route. Shared registered answering respects model health,
  language/context, and explicit flags that forbid implicit source processing.

The final model snapshot was auto-merged and validated against EF's current
model. New regressions cover context precedence, Hindi/Hinglish, offline cached
answering and structured-only requests with no PDF processing/inference.

Global Assistant adds Python package members and changes shared runtime files.
Therefore the question-service inventory/digest and its office verification pin
were regenerated for this integrated tree. The inventory records both accepted
Court and Assistant SHAs. The accepted V3 model identity, weights/runtime hashes,
settings, portable manifests, transport receipt and recovery-controller bytes
remain unchanged. Model/question endpoints remain `127.0.0.1:8096` and
`127.0.0.1:8097`, with no cloud fallback.

The strict application TypeScript build exposed existing errors in the accepted
UI tree. Small compile corrections remove retired legacy-Khasra setters and
unused imports, use the actual Court timeline DTO fields, distinguish DOM and
React mouse events, and allow nullable React refs. Retained legacy components
are exported without changing active routes. No CSS/design redesign was made;
document/draft/outward behavior and the accepted navigation remain intact.

## Preserved domain contracts

- Matter supports multiple canonical Awards and zero or one explicitly selected
  primary. `MatterAward.IsPrimary` remains false by default; legacy `awardId` and
  explicit `primaryAwardId` promotion remain supported. Canonical Khasra, Court,
  Dak and responsibility relationships remain explicit, with no inferred links.
- Village Matter creation uses canonical Court selections and an explicit
  Workstream. WorkItem assignees use `displayName`; activity fields/order,
  successful link/unlink context refresh, clickable canonical Khasras and
  read-only legacy Khasra text are preserved.
- Smart Core Intake remains staged, local, officer-confirmed and canonical.
  Acquisition History groups events by explicit Award relationships and returns
  unlinked events separately; chronology cannot invent Award membership.
- Court source validation/Fast Path, cached grounded intelligence, language,
  zero-evidence pending status, passive notices and specific
  `ModelInsufficientMemory` precedence remain covered by the accepted suites.
- Global Assistant is the accepted backend foundation. No new assistant design
  or unrelated feature was added.

## Migration safety

There are 48 unique migration IDs. No historical migration was edited and no
new integration migration was required. The three feature migrations are:

- `20261004155206_AddAssistantConversations`
- `20261004181412_AddSmartCoreDocumentIntake`
- `20261004222119_AddMatterCanonicalContext`

Acquisition History is a read-only query/API and has no schema migration.
EF's pending-model check reports no changes since the last migration. The
complete generated migration chain applied successfully inside a random,
disposable local PostgreSQL schema, which was dropped afterward. Existing
application/office rows were not rewritten.

Normal relational startup runs `MigrateAsync` then seeding when
`Startup:RunDatabaseBootstrap` is enabled (the default). Tests use their
configured in-memory/disposable stores. The opt-out skips migration and seed;
background-worker controls are separate. An office release still requires its
normal database backup/change procedure before starting an updated application.

## Security audit

The versioned integration tree was searched for fixed account/database passwords,
test login credentials, personal login fallbacks, API/access tokens and private
keys. Existing fixed test login passwords were replaced with cryptographically
generated runtime values; disposable JWT fixtures also generate their secrets.
Environment configuration remains the source of application and smoke credentials.
The final scan contains no credential candidates. Secret values were not included
in this report. Documentation placeholders and inert invalid-hash/redaction
fixtures are not application credentials.

## Validation

The full serial .NET suite passed 913 tests with zero failures (17 minutes,
53 seconds). This includes 193 Court/DHC tests, 70 Matter tests, 23 Smart Intake
tests, 13 Acquisition History tests and 15 Global Assistant tests. The two
opt-in PostgreSQL checks are exercised separately in fresh test hosts.

Smart Intake's opt-in PostgreSQL migration/concurrent-confirmation regression
also passed independently (one test, 32 seconds).
Matter's opt-in PostgreSQL migration, atomic rollback, concurrent-link and
single-primary regression passed independently (one test, 21 seconds).
The final build's targeted ONLYOFFICE suite also passed all 34 tests after the
generated-secret fixture adjustment. No disposable migration/test schemas remain.

- .NET solution build passed with zero errors and three existing xUnit analyzer
  warnings.
- EF pending-model validation passed; all 48 migrations applied in disposable
  local PostgreSQL and cleanup completed.
- `npx tsc --noEmit`, the application/project build `tsc -b`, and Vite production
  build passed. Vite reports the existing large-bundle warning.
- All 127 frontend contract/integration tests passed, including Matter, Court,
  Land and editor/calculator contracts.
- 283 available Python tests passed with mocked providers/synthetic evidence;
  final changed fixture/context tests were also rerun.
- Office runtime PowerShell tests passed 58 assertions; V3 transfer tests passed
  23 assertions, including unchanged recovery-controller bytes and no model launch.
- Secure authenticated Matter smoke was not run: neither required credential
  environment variable was available. The accepted smoke script has no login
  fallback.
- One optional Python module, `test_pilot_recovery`, cannot be imported because
  `gguf` is absent. This is a packaging/recovery test dependency; it is reported
  separately from the passing question-service/runtime tests. No model was started
  and no inference was performed.

The first combined .NET run exhausted the constrained test-host heap during a
PostgreSQL migration test. The final full suite uses serial execution, with the
two heavy PostgreSQL regressions executed in fresh hosts. This resource failure
is recorded separately from final product-test results.

## Before office deployment

No office deployment/health check or live model inference was attempted. Live
authenticated smoke requires local environment credentials; the optional GGUF
test requires its dependency. Office-specific backup, package/configuration,
ACL, service and health verification remain release operations for an authorized
deployment. This integration does not bypass those checks or start services.
