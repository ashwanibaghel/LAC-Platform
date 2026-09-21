namespace LAC.Domain;

using System;

public sealed class CourtCaseMatter : OfficialRecord
{
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid MatterId { get; set; }
    public Matter Matter { get; set; } = null!;
}

public sealed class CourtCaseParty : OfficialRecord
{
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public string? FatherOrSpouseName { get; set; }
    public string? AddressText { get; set; }
    public string? Remarks { get; set; }
    public int Sequence { get; set; }
}

public sealed class CourtCaseRepresentative : OfficialRecord
{
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid? CourtCasePartyId { get; set; }
    public CourtCaseParty? CourtCaseParty { get; set; }
    public string DisplayName { get; set; } = "";
    public string RepresentativeType { get; set; } = "Counsel";
    public string? RepresentsRole { get; set; }
    public string? ContactText { get; set; }
    public string? Remarks { get; set; }
}

public sealed class CourtCaseDocument : OfficialRecord
{
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public Guid? CourtProceedingId { get; set; }
    public CourtProceeding? CourtProceeding { get; set; }
    public string? DocumentRole { get; set; }
    public string? DisplayName { get; set; }
}

public enum CourtCaseAction
{
    Created = 0,
    MetadataUpdated = 1,
    StatusChanged = 2,
    ResponsibilityChanged = 3,
    AwardLinked = 4,
    AwardUnlinked = 5,
    KhasraLinked = 6,
    KhasraUnlinked = 7,
    MatterLinked = 8,
    MatterUnlinked = 9,
    PartyAdded = 10,
    PartyUpdated = 11,
    PartyRemoved = 12,
    RepresentativeAdded = 13,
    RepresentativeUpdated = 14,
    RepresentativeRemoved = 15,
    DocumentUploaded = 16,
    DocumentLinked = 17,
    DocumentUnlinked = 18,
    ProceedingRecorded = 19
}

public sealed class CourtCaseEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourtCaseId { get; set; }
    public CourtCase CourtCase { get; set; } = null!;
    public int SequenceNumber { get; set; }
    public CourtCaseAction Action { get; set; }
    public DateTimeOffset ActionAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid ActorUserId { get; set; }
    public AppUser? ActorUser { get; set; }
    public string ActorDisplayNameSnapshot { get; set; } = "";
    public string? ActorDesignationSnapshot { get; set; }

    public string CaseNumberSnapshot { get; set; } = "";
    public string? CaseTitleSnapshot { get; set; }

    public Guid? WorkstreamIdSnapshot { get; set; }
    public string? WorkstreamNameSnapshot { get; set; }

    public Guid? SourceDeskId { get; set; }
    public string? SourceDeskNameSnapshot { get; set; }
    public Guid? SourceUserId { get; set; }
    public string? SourceUserDisplayNameSnapshot { get; set; }

    public Guid? TargetDeskId { get; set; }
    public string? TargetDeskNameSnapshot { get; set; }
    public Guid? TargetUserId { get; set; }
    public string? TargetUserDisplayNameSnapshot { get; set; }

    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }

    public Guid? AwardId { get; set; }
    public Guid? KhasraId { get; set; }
    public Guid? MatterId { get; set; }
    public Guid? CourtCasePartyId { get; set; }
    public Guid? CourtCaseRepresentativeId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? CourtProceedingId { get; set; }

    public string? Reason { get; set; }
    public string? Notes { get; set; }
}
