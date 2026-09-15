using LAC.Domain;

public readonly record struct MatterDraftLayout(string PageSize, string Orientation, decimal MarginTopMm, decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm);

public static class MatterDraftLayoutProfiles
{
    // Provisional calibration only: update this one profile after measuring the real office noting sheet.
    public static readonly MatterDraftLayout NotingSheetV1Provisional = new("A4", "Portrait", 25m, 20m, 20m, 25m);

    // Official alias matching the centralized DelhiLacNotingV1 terminology
    public static readonly MatterDraftLayout DelhiLacNotingV1 = NotingSheetV1Provisional;

    public static MatterDraftLayout For(MatterDraft draft) => draft.DraftType == MatterDraftType.Noting
        ? DelhiLacNotingV1
        : new MatterDraftLayout(draft.PageSize, draft.Orientation, draft.MarginTopMm, draft.MarginRightMm, draft.MarginBottomMm, draft.MarginLeftMm);
}

