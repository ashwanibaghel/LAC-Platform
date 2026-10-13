namespace LAC.Domain;

public sealed class MatterOfficeSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? SourceDakId { get; set; }
    public Guid ActorSessionVersion { get; set; }
    public int WorkingRevision { get; set; }
    public int Revision { get; set; } = 1;
    public string State { get; set; } = "Active";
    public DateTimeOffset ExpiresAt { get; set; }
    public string DocumentKey { get; set; } = "";
    public Guid? LatestSnapshotId { get; set; }
    public string CompositionVersion { get; set; } = "OfficeNoteV1";
    public string? HistoryManifestJson { get; set; }
    public string? HistoryManifestHash { get; set; }
    public string? HistoryManifestSeal { get; set; }
}

public sealed class MatterOfficeSaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public int ClientGeneration { get; set; }
    public int SessionRevision { get; set; }
    public string State { get; set; } = "Pending";
    public DateTimeOffset Deadline { get; set; }
    public Guid? SnapshotId { get; set; }
    public string? FailureCode { get; set; }
    public string? NativeCaptureStoragePath { get; set; }
    public string? NativeCaptureSha256 { get; set; }
    public long? NativeCaptureSize { get; set; }
    public string? NativeCanonicalHash { get; set; }
    public string? NativeContentHash { get; set; }
}

public sealed class MatterOfficeSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? SaveRequestId { get; set; }
    public int Version { get; set; }
    public string StoragePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatterOfficePreview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid SessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid? SourceDakId { get; set; }
    public int WorkingRevision { get; set; }
    public string ConverterVersion { get; set; } = "OfficeNoteV1";
    public string ContentJson { get; set; } = "";
    public string CanonicalText { get; set; } = "";
    public string CanonicalHash { get; set; } = "";
    public string? NativeCaptureJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatterOfficeNoteEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public Guid PreviewId { get; set; }
    public Guid SnapshotId { get; set; }
    public string Sha256 { get; set; } = "";
    public string ConverterVersion { get; set; } = "";
    public string LayoutPolicy { get; set; } = "GreenLegalV2";
}

public sealed class MatterGreenSheetSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? WorkingPreviewId { get; set; }
    public string ManifestJson { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatterGreenSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public string PdfStoragePath { get; set; } = "";
    public string PdfSha256 { get; set; } = "";
    public string RendererVersion { get; set; } = "";
    public string FontManifestSha256 { get; set; } = "";
    public int PageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
