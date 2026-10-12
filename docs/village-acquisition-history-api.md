# Village acquisition history

Base: `ab504c4e499b2309e4d0b52b959cda29a3bab6be` (accepted Land backend including Smart Core Intake).
Branch: `codex/village-award-acquisition-history`.

## Endpoint and permissions

`GET /api/villages/{villageId}/acquisition-history`

Requires authenticated `Village.View` and `Award.View` in the `AWARD` workstream (or All scope). Possession events additionally require `Award.View` in `POSSESSION`, matching the existing possession read endpoint. When that permission is absent, possession events are omitted and `possessionEventsVisible` is `false`; omission must not be displayed as zero possession events.

Returns 200 for an active Village, including an empty Village. Missing, inactive, or archived Villages return 404. Unauthenticated callers receive 401; callers lacking either required permission receive 403. The read does not save data, create links, or repair relationships. No schema migration is required.

## Exact JSON shape

```json
{
  "villageId": "<village-uuid>",
  "awards": [
    {
      "awardId": "<award-uuid>",
      "awardNumber": "30/2002-03",
      "awardDate": "2002-12-09",
      "events": [
        {
          "eventId": "<notification-uuid>",
          "type": "Notification",
          "date": "2000-12-01",
          "section": "4",
          "notificationId": "<notification-uuid>",
          "possessionEventId": null,
          "reference": "<canonical notification number>",
          "eventType": null,
          "status": null,
          "relationshipBasis": "ExplicitAwardNotificationLink",
          "isShared": false,
          "linkedAwardIds": ["<award-uuid>"]
        },
        {
          "eventId": "<award-uuid>",
          "type": "Award",
          "date": "2002-12-09",
          "section": null,
          "notificationId": null,
          "possessionEventId": null,
          "reference": "30/2002-03",
          "eventType": null,
          "status": "Draft",
          "relationshipBasis": "AwardRecord",
          "isShared": false,
          "linkedAwardIds": ["<award-uuid>"]
        }
      ]
    }
  ],
  "unassignedEvents": [],
  "possessionEventsVisible": true
}
```

The example date for the notification is illustrative, not a claim about a real Village's notification. All response dates and values come from canonical records. Properties always appear; nullable values are JSON null. UUIDs are strings; dates are `YYYY-MM-DD`. Arrays are never null.

Each event has exactly the fields shown above:

| Field | Type / semantics |
| --- | --- |
| `eventId` | Canonical Award, Notification, or PossessionEvent UUID; use `(type, eventId)` as event identity. |
| `type` | `Award`, `Notification`, or `Possession`. |
| `date` | Nullable canonical AwardDate, NotificationDate, or PossessionDate. |
| `section` | Canonical Notification.SectionType for a notification; otherwise null. No section-label rewriting. |
| `notificationId` | Notification UUID for notifications; otherwise null. |
| `possessionEventId` | PossessionEvent UUID for possession; otherwise null. |
| `reference` | Canonical AwardNumber or NotificationNumber; null for possession because its model has no reference-number field. |
| `eventType` | Canonical PossessionEvent.EventType; otherwise null. |
| `status` | Canonical Award.Status or PossessionEvent.Status; null for notifications. |
| `relationshipBasis` | One of the four values described below. |
| `isShared` | True for notifications with more than one distinct explicit Award owner. False for Award/possession events. |
| `linkedAwardIds` | Sorted distinct UUIDs of explicit Award owners. For Award/possession, the single owner. For unassigned notifications, an empty array. |

## Attribution rules

An active Award is included in the Village response when an explicit `AwardVillage` link exists, or an explicit `AwardKhasra` link references an active Khasra in that Village. These are the existing Village workspace membership paths. An Award linked through both paths appears once.

Within each Award group:

- `AwardRecord`: the Award itself, using its own number/date/status.
- `ExplicitAwardNotificationLink`: only notifications with an `AwardNotification` relationship to that exact Award ID.
- `ExplicitPossessionAwardLink`: only possession events whose `PossessionEvent.AwardId` equals that exact Award ID.

No date comparison, chronology, common project, common Village, parcel overlap, supplementary-parent reference, filename, document-review session, or LR-row co-occurrence attributes an event to an Award. An explicit notification dated after the Award is still included; an earlier notification without that relationship is never included in the Award group.

Only active canonical records are rendered. Archived/inactive Awards, Notifications, PossessionEvents, and parcel membership paths are omitted. Undated records are retained with `date: null`; dates are never substituted from creation/upload/review timestamps.

## Unassigned notifications

An active notification explicitly linked through `NotificationKhasra` to an active Village Khasra, but with **no `AwardNotification` link to any Award**, appears once in `unassignedEvents`:

```json
{
  "eventId": "<notification-uuid>",
  "type": "Notification",
  "date": null,
  "section": "6",
  "notificationId": "<notification-uuid>",
  "possessionEventId": null,
  "reference": "<canonical notification number>",
  "eventType": null,
  "status": null,
  "relationshipBasis": "NoExplicitAwardNotificationLink",
  "isShared": false,
  "linkedAwardIds": []
}
```

UI label: **Not yet linked to an Award**. This is a cleanup state, not an error. Multiple parcel links do not duplicate the event. Committed LR notifications participate through their existing canonical NotificationKhasra links; draft LR rows and extracted document proposals are not acquisition events. The possession model requires an Award owner, so there are no unassigned canonical possession events in this version.

A Village-parcel notification linked only to an Award outside the response is neither assigned to a local Award nor falsely labelled globally unassigned. Its existing outside-Award relationship is preserved; it is outside this endpoint's Award groups. Links to archived Awards likewise prevent a false globally-unassigned label.

## Shared notifications

A shared notification appears under each included Award with an explicit link to it, retaining the same `eventId`/`notificationId`. `isShared` is true and `linkedAwardIds` lists all explicit owners, including outside-Village or archived owners. It never appears under an unlinked Award. This is repeated representation of one canonical notification, not duplicated stored data.

## Ordering and UI consumption

Award groups are ordered by Award date ascending, then Award number using ordinal comparison, then Award ID. Undated groups come last.

Each group's events and `unassignedEvents` are ordered by date ascending, undated last. Same-date ties use ordinal event type, then canonical event ID for deterministic ordering. No ordering rule establishes attribution.

The Village UI should render separate Award histories from this endpoint and a separate unassigned section. Do not reconstruct a continuous acquisition chain from the existing Overview endpoint's flat Award/notification inventories. Those inventories remain compatible for non-timeline uses. Court/matter/review events are not included in this version; any future extension needs authoritative explicit relationships and its own access checks.

## Regression validation

`VillageAcquisitionHistoryTests` creates the two Award dates in the reported regression (1986 and 2002), shared Village/parcel membership, and Sections 4/6/17 linked only to the 2002 Award. It verifies that no notification enters the 1986 Award's history. Additional tests cover unassigned records, shared records, explicit possession ownership, chronological/undated ordering, read-only behavior, archived/unrelated records, missing Villages, and endpoint permissions.

```powershell
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter 'FullyQualifiedName~VillageAcquisitionHistory'
dotnet test tests/LAC.Tests/LAC.Tests.csproj --filter 'FullyQualifiedName~VillageAcquisitionHistory|FullyQualifiedName~CoreDocument|FullyQualifiedName~AwardWorkflow|FullyQualifiedName~LrWorkflow|FullyQualifiedName~RbacTests|FullyQualifiedName~ApiNavigationTests|FullyQualifiedName~AwardIngestionTests'
```

Verified: 13 focused history tests and 117 relevant Land/Award, Smart Intake, API, and RBAC tests passed. The relevant run also enabled the existing isolated local PostgreSQL Smart Intake migration/concurrency check with `CORE_INTAKE_POSTGRES_SMOKE=1`.
