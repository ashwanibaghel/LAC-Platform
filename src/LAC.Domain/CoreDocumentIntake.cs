namespace LAC.Domain;

/// <summary>A local proposal and its immutable source evidence, never a canonical Award.</summary>
public sealed class CoreDocumentIntake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VillageId { get; set; }
    public Village Village { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string Sha256Hash { get; set; } = "";
    public string Status { get; set; } = "Staged";
    public string ProposalJson { get; set; } = "{}";
    public string SourcePagesJson { get; set; } = "[]";
    public string ClassifierVersion { get; set; } = "core-native-v1";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public Guid? ConfirmedAwardId { get; set; }
    public Award? ConfirmedAward { get; set; }
    public string? ConfirmedRole { get; set; }
    public Guid? ConfirmedDocumentId { get; set; }
    public Document? ConfirmedDocument { get; set; }
    public string? ConfirmationJson { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    public int Revision { get; set; }
}
