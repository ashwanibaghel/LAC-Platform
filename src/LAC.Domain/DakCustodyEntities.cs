namespace LAC.Domain;

public enum DakRoutingState { Unassigned, WithHolder, InTransit, LegacyUnconfirmed }
public enum DakPhysicalState { Unknown, NotPresent, AtRecordedLocation, Held, InTransit, ReturnPending }
public enum DakTransferState { Pending, Received, PulledBack }
public enum DakDestinationKind { Officer, RecordRoom }
public enum OfficeDeskPurpose { General, RecordRoom }

// Mutable delivery projection. Dispatch identity is frozen; every outcome has an immutable movement.
public sealed class DakTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DakId { get; set; }
    public Dak Dak { get; set; } = null!;
    public DakMovementAction Purpose { get; set; }
    public DakStatus FromStatus { get; set; }
    public DakRoutingState FromRoutingState { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid? FromDeskId { get; set; }
    public Guid? FromHolderUserId { get; set; }
    public Guid ToDeskId { get; set; }
    public Guid ToUserId { get; set; }
    public DakDestinationKind DestinationKind { get; set; }
    public bool IncludesPhysicalOriginal { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? Remarks { get; set; }
    public string? Instructions { get; set; }
    public DakTransferState State { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? PhysicalReceivedAt { get; set; }
    public DateTimeOffset? PulledBackAt { get; set; }
    public string? PullBackReason { get; set; }
    public DateTimeOffset? PhysicalReturnedAt { get; set; }
    public string? PhysicalReturnProvenance { get; set; }
}

// Append-only durable result of one actor/request pair, independent of later aggregate state.
public sealed class DakWorkflowCommandReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DakId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public string Action { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
