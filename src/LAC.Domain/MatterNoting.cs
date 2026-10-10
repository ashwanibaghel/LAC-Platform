namespace LAC.Domain;

public enum DakVillageClassification { Unclassified, General, VillageSpecific, MultiVillage }

public sealed class MatterWorkingNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid AuthorUserId { get; set; }
    public int Revision { get; set; }
    public string ContentJson { get; set; } = "";
    public string CanonicalText { get; set; } = "";
    public Guid? SourceDakId { get; set; }
    public string CitationsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MatterOfficialNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public int Number { get; set; }
    public int Version { get; set; } = 1;
    public string ContentJson { get; set; } = "";
    public string CanonicalText { get; set; } = "";
    public string TextHash { get; set; } = "";
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Designation { get; set; } = "";
    public Guid? DeskId { get; set; }
    public string? DeskName { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public Guid? SourceDakId { get; set; }
    public string CitationsJson { get; set; } = "[]";
    public string DocumentManifestJson { get; set; } = "[]";
}

public sealed class MatterNoteRemark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid NoteId { get; set; }
    public string AnchorJson { get; set; } = "";
    public string Text { get; set; } = "";
    public string State { get; set; } = "Open";
    public int Revision { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Designation { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? AddressedByUserId { get; set; }
    public DateTimeOffset? AddressedAt { get; set; }
}

public sealed class MatterPdfAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatterId { get; set; }
    public Guid DocumentId { get; set; }
    public int DocumentVersion { get; set; }
    public string? DocumentHash { get; set; }
    public int Page { get; set; }
    public string RegionJson { get; set; } = "";
    public string? Quote { get; set; }
    public string? Text { get; set; }
    public Guid AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MatterWorkflowReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
