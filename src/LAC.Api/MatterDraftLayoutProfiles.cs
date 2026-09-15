using LAC.Domain;

public readonly record struct MatterDraftLayout(string PageSize, string Orientation, decimal MarginTopMm, decimal MarginRightMm, decimal MarginBottomMm, decimal MarginLeftMm);

public static class MatterDraftLayoutProfiles
{
    // Provisional calibration only: update this one profile after measuring the real office noting sheet.
    public static readonly MatterDraftLayout NotingSheetV1Provisional = new("A4", "Portrait", 25m, 20m, 20m, 25m);

    public static MatterDraftLayout For(MatterDraft draft) => draft.DraftType == MatterDraftType.Noting
        ? NotingSheetV1Provisional
        : new MatterDraftLayout(draft.PageSize, draft.Orientation, draft.MarginTopMm, draft.MarginRightMm, draft.MarginBottomMm, draft.MarginLeftMm);
}
