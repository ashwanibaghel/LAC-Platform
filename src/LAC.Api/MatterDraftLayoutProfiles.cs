using LAC.Domain;

public readonly record struct MatterDraftLayout(string PageSize, string Orientation, decimal MarginTopMm, decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm);

public static class MatterDraftLayoutProfiles
{
    // Fixed physical paper profile. The 45 mm mirrored gutter is derived by
    // page parity in the frontend layout profile, not stored as mutable margins.
    public static readonly MatterDraftLayout DelhiLacNotingLegalMirrorV1 = new("Legal", "Portrait", 25m, 0m, 25m, 0m);

    public static MatterDraftLayout For(MatterDraft draft) => draft.DraftType == MatterDraftType.Noting
        ? DelhiLacNotingLegalMirrorV1
        : new MatterDraftLayout(draft.PageSize, draft.Orientation, draft.MarginTopMm, draft.MarginRightMm, draft.MarginBottomMm, draft.MarginLeftMm);
}

