namespace LAC.Domain;

/// <summary>A read projection of canonical relationships; not a Village-wide acquisition chain.</summary>
public sealed record VillageAcquisitionHistory(
    Guid VillageId,
    IReadOnlyList<AwardAcquisitionHistory> Awards,
    IReadOnlyList<AcquisitionHistoryEvent> UnassignedEvents,
    bool PossessionEventsVisible,
    IReadOnlyList<AcquisitionHistoryEvent>? TemporaryEvents = null);

public sealed record AwardAcquisitionHistory(
    Guid AwardId,
    string AwardNumber,
    DateOnly? AwardDate,
    IReadOnlyList<AcquisitionHistoryEvent> Events);

public sealed record AcquisitionHistoryEvent(
    Guid EventId,
    string Type,
    DateOnly? Date,
    string? Section,
    Guid? NotificationId,
    Guid? PossessionEventId,
    string? Reference,
    string? EventType,
    string? Status,
    string RelationshipBasis,
    bool IsShared,
    IReadOnlyList<Guid> LinkedAwardIds,
    string? SourcePath = null,
    IReadOnlyList<AcquisitionHistoryDocument>? Documents = null,
    IReadOnlyList<string>? Warnings = null);

public sealed record AcquisitionHistoryDocument(Guid DocumentId, string ContentPath);
