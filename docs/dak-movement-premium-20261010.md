# Premium Dak movement and recipient eligibility

Branch: `codex/dak-movement-premium-20261010`

Exact base and parent: `bff45037a596c222ce4573b8eb56ca4d9cbf95f9`.

## Independently verified blocker

Read-only transactions against the existing manual acceptance database found **0 configured Office Desks, 0 active desks, 0 desk memberships and 6 accounts**. There are existing Dak.Receive role grants, but grants alone do not establish operational desk membership. For an authorized request, the existing movement-targets endpoint consequently has no desks to return. No real desk or recipient was invented or provisioned.

The old modal rendered an empty native desk select without a placeholder, configuration explanation, loading state or differentiated authorization/API error state. This made a real configuration gap appear to be a broken form.

The lookup also included any active desk member, without checking that the member could accept the delivery. RECEIVE already requires an operational Dak.Receive scope and live work responsibility. The corrected lookup applies those existing bounds, including Helper parent/delegation checks, before presenting recipients. Sending also rechecks recipient eligibility under the Dak transaction so a stale lookup or crafted recipient ID cannot create a transfer to an unauthorized member.

## Product changes

- A scoped movement stylesheet matches the accepted RBAC modal language: white card, restrained navy accents, numbered sections, compact Dak identity header, soft backdrop, scrollable body and fixed footer.
- Header shows the diary number, subject and sender. Long text does not create horizontal overflow.
- Office Desks are searchable by name/code using a search field and keyboard-accessible native selection. Desk choice and recipient confirmation are explicit; no default desk silently receives the Dak.
- Recipient choices come only from eligible members of the selected canonical desk. Record Room destination kind is derived from its configured desk purpose.
- Loading, no configured desks, no eligible members, search misses, expired/unauthorized access and retryable API errors have distinct explanations. Send stays disabled until a valid destination and recipient are selected.
- Physical dispatch is available only when the workspace confirms an original in the actor's custody at an active membership desk. The footer identifies digital-only versus digital-and-paper dispatch. It does not declare receipt or recovered paper on dispatch/pull-back.
- Optional Instructions and Official Noting remain text fields with their existing limits and request contract. No voice feature was added.
- Forward, Return, Receive, Pull Back and physical-return confirmation use the same layout. Resolve, Reopen and Cancel retain their existing validation while sharing the styling.
- Dialog focus is contained, Escape closes only while no mutation is pending, prior focus is restored, and fields/buttons have accessible names. A synchronous ref prevents duplicate mutation handlers. An unchanged retry retains its secure UUID request key; changed command input obtains a new key.
- The existing outward-reply read in the Dak workspace now supplies the endpoint's required page/pageSize parameters. Genuine API checks exposed its previous 400 response. The Outward API and feature implementation were not changed; existing cross-Dak reply-isolation assertions remain intact.

## Backend and workflow boundaries

`GET /api/dak/{id}/movement-targets` keeps its response shape and existing scoped Dak.Mark/Dak.Move authorization. Authorized desk members/monitors retain lookup read access; lookup access never grants holder custody. Active desks with zero eligible recipients remain visible for configuration diagnosis.

Eligibility requires an active user, desk and live membership; an active role granting Dak.Receive with All, Assigned or Workstream scope; and the same live work responsibility used by receipt authorization. Own-only scope does not authorize a pending delivery. Helpers retain their existing parent/delegation ceilings. Permission, membership and responsibility changes after lookup are rechecked by dispatch. Shared locks additionally protect recipient role/permission/allocation rows when dispatching to another officer.

Existing command endpoints and payloads remain: transfers, receive, pull-back, confirm-return, resolve, reopen and cancel. Initial marking still requires Dak.Mark. Only the confirmed holder can forward or return. Send leaves the Dak In Transit; Receive transfers custody; pull-back is available only before receipt; physically departed paper remains ReturnPending until actual recovery is confirmed. Registration identity, immutable timeline, technical GUID keys and physical/digital independence are retained.

No new permissions, domain entities, schema or migrations were added. No designation names or named officers are used for routing. No Matter production implementation, RBAC architecture, main/integration/Anti worktree or office deployment was changed.

## Exact changed files

1. `src/LAC.Api/DakEndpoints.cs`
2. `src/LAC.Infrastructure/DakCustodyWorkflow.cs`
3. `src/LAC.Infrastructure/DakRecipientEligibility.cs`
4. `src/LAC.Web/src/dak/DakMovementModal.tsx`
5. `src/LAC.Web/src/dak/movement.css`
6. `src/LAC.Web/src/dak/DakDetailWorkspace.tsx`
7. `src/LAC.Web/tests/dak-movement-premium.test.mjs`
8. `src/LAC.Web/tests/dak-movement-real.acceptance.mjs`
9. `src/LAC.Web/tests/dak-v2-intake-workspace.test.mjs`
10. `tests/LAC.Tests/DakMovementTargetsTests.cs`
11. `tests/LAC.Tests/DakCustodyTests.cs`
12. `tests/LAC.Tests/DakDirectoryTests.cs`
13. `tests/LAC.Tests/DakTestCustodyFixtures.cs`
14. `tests/LAC.Tests/DakTests.cs`
15. `docs/dak-movement-premium-20261010.md`

Existing synthetic fixtures now configure receipt authority/responsibility before dispatch, instead of giving receipt permission afterward. Retry fixtures still exercise all original assertions and fault injection, with their responsibility declared before interceptors are installed. The two SPA outward mocks now validate the required pagination query contract; their isolation assertions were not weakened.

## Validation evidence

| Check | Final result |
|---|---|
| Combined Dak/Matter backend regressions | 215 passed, 0 failed, 0 skipped |
| Dak selection within that run | 133 passed: 111 non-PostgreSQL + 22 PostgreSQL |
| Matter/non-Dak selection | 82 passed |
| New eligibility/operational-lookup selection | 12 passed, including existing authorized non-holder lookup access |
| Workflow retry/commit-ambiguity selection | 10 passed |
| New premium modal browser states | 7 passed |
| Complete frontend suite | 331 passed, 0 failed, 0 skipped |
| TypeScript/Vite production build | Passed; existing bundle-size warning |
| git diff --check | Passed |
| Real production-bundle browser | Passed at 1366x768, DPR 1, 12 screenshots, zero page exceptions |

The final backend run uses an external serial runsettings file after an earlier parallel test-host stack overflow. No test was disabled or skipped. The PostgreSQL suite runs on a newly initialized, task-owned cluster at **127.0.0.1:55446**, with generated disposable databases. It includes the existing migration/preflight, receive/pull-back races, physical custody and immutable-history checks, plus real-cookie target/send/receive/Record Room/return/pull-back checks and permission revocation after lookup. Disk synchronization was relaxed only for these disposable tests; this is not a power-loss durability test.

Genuine browser acceptance serves the built `dist` bundle at **127.0.0.1:5196**, proxying only the task-owned API at **127.0.0.1:5197** and disposable synthetic database. It verifies Unmarked registration, empty desk explanation, ineligible-member explanation, one transfer under a double click, nominated receipt, holder/timeline updates, digital movement while paper remains at its original location, Record Room acceptance, Return/Pull Back, mandatory physical receipt and ReturnPending followed by actual recovered paper. The final asset is `index-Da_8MTFP.js`; screenshot evidence records the actual script URLs and viewport for every capture.

Logs/TRX evidence: `E:/LAC-Dak-Movement-20261010-64d89a/`.

## Screenshots

All captures are outside the repository at `C:/Users/ashwa/.codex/visualizations/2026/10/10/dak-movement-premium-20261010/`.

| Capture | Evidence |
|---|---|
| `01-new-unmarked-dak.png` | Successfully registered, unassigned receipt |
| `02-empty-office-configuration.png` | Clear missing-desk explanation and blocked send |
| `03-desk-without-eligible-member.png` | Explicit recipient configuration gap |
| `04-initial-mark-confirmation.png` | Destination/officer/digital-only confirmation |
| `05-receive-digital-dak.png` | Digital receipt preserves the paper location |
| `06-received-holder-and-timeline.png` | Updated operational holder and immutable noting |
| `07-forward-to-record-room.png` | Configured Record Room/caretaker destination |
| `08-return-to-officer.png` | Consistent Return dialog |
| `09-pull-back-before-receipt.png` | Mandatory pull-back reason |
| `10-initial-mark-with-physical-original.png` | Explicit paper dispatch choice |
| `11-physical-receipt-attestation.png` | Required acknowledgment of actual paper receipt |
| `12-confirm-actual-paper-recovery.png` | Confirm return only after recovered paper |

`browser-evidence.json` in that directory records final pass status, asset URLs, viewport dimensions and chronological workflow events. Screenshots are genuine unmodified browser captures; representative modal states were visually inspected for fit and overflow.

## Safety and remaining blocker

Before/after read-only fingerprints of manual AppUsers, UserRoles, RolePermissions, OfficeDesks and UserDeskMemberships are identical. The six accounts, their permissions and desk configuration are unchanged. Acceptance backend **5190 PID 2420** and frontend **5191 PID 26008** were never restarted. No login or mutation request used the manual accounts/database.

The office configuration blocker deliberately remains: an authorized office administrator must configure real desks, eligible memberships and receipt responsibilities through the existing approved administration workflow. This fix explains and safely blocks that gap; it does not fabricate routing data.

Task-owned browser/API/database resources are cleaned up before push. No merge, deployment or update of the existing runtime is included. Review is required before any runtime update.
