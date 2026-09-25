namespace LAC.Domain;

/// <summary>Authoritative physical layout policy for a Matter Draft document class.</summary>
public readonly record struct MatterDraftLayoutProfile(
    string Code,
    string PageSize,
    string Orientation,
    bool MirrorMargins,
    decimal MarginInsideMm,
    decimal MarginOutsideMm,
    decimal MarginTopMm,
    decimal MarginBottomMm,
    decimal GutterMm);

public static class MatterDraftOfficeProfiles
{
    public static readonly MatterDraftLayoutProfile DelhiLacNotingV1 = new(
        Code: "DelhiLacNotingV1",
        PageSize: "Legal",
        Orientation: "Portrait",
        MirrorMargins: true,
        MarginInsideMm: 45m,
        MarginOutsideMm: 20m,
        MarginTopMm: 25m,
        MarginBottomMm: 20m,
        GutterMm: 0m);
}
