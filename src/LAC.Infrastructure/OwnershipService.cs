using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public sealed class OwnershipWorkflowException(string message, int statusCode = 400) : Exception(message) { public int StatusCode { get; } = statusCode; }
public sealed record ShareValidationResult(bool IsComplete, bool IsValid, int? TotalNumerator, int? TotalDenominator, string Message);
public sealed record RecordedOwner(Guid PartyId, string DisplayName, string? RawShareText, int? Numerator, int? Denominator);
public sealed record RecordedOwnershipResult(bool Found, bool IsAmbiguous, string? Message, Guid? KhatauniRecordId, Guid? KhataId, IReadOnlyList<RecordedOwner> Owners);

public sealed class OwnershipService(LacDbContext db)
{
    public static ShareValidationResult ValidateShares(IEnumerable<KhataPartyShare> shares)
    {
        var rows = shares.ToList();
        if (rows.Count == 0 || rows.Any(x => x.ShareNumerator is null || x.ShareDenominator is null || x.ShareNumerator < 0 || x.ShareDenominator <= 0))
            return new(false, false, null, null, "Ownership share validation incomplete.");
        var denominator = rows.Select(x => x.ShareDenominator!.Value).Aggregate(Lcm);
        var numerator = rows.Sum(x => x.ShareNumerator!.Value * (denominator / x.ShareDenominator!.Value));
        var reduced = Reduce(numerator, denominator);
        return numerator == denominator
            ? new(true, true, reduced.Numerator, reduced.Denominator, "Shares total 100%.")
            : new(true, false, reduced.Numerator, reduced.Denominator, $"Recorded ownership shares total {numerator * 100m / denominator:0.##}%; expected 100%.");
    }

    public async Task<Party> CreatePartyAsync(PartyType partyType, string displayName, string? fatherOrSpouseName, string? addressText, string? remarks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new OwnershipWorkflowException("Display name is required.");
        var party = new Party { PartyType = partyType, DisplayName = displayName.Trim(), FatherOrSpouseName = Clean(fatherOrSpouseName), AddressText = Clean(addressText), Remarks = Clean(remarks) };
        db.Parties.Add(party); await db.SaveChangesAsync(ct); return party;
    }

    public async Task<Khata> CreateKhataAsync(Guid khatauniRecordId, string khataNumber, string? rawKhataNumber, string? remarks, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(khataNumber)) throw new OwnershipWorkflowException("Khata number is required.");
        if (!await db.KhatauniRecords.AnyAsync(x => x.Id == khatauniRecordId, ct)) throw new OwnershipWorkflowException("Khatauni record was not found.", 404);
        var existing = await db.Khatas.SingleOrDefaultAsync(x => x.KhatauniRecordId == khatauniRecordId && x.KhataNumber == khataNumber.Trim(), ct);
        if (existing is not null) return existing;
        var khata = new Khata { KhatauniRecordId = khatauniRecordId, KhataNumber = khataNumber.Trim(), RawKhataNumber = Clean(rawKhataNumber), Remarks = Clean(remarks) };
        db.Khatas.Add(khata); await db.SaveChangesAsync(ct); return khata;
    }

    public async Task<KhataKhasra> LinkKhasraAsync(Guid khataId, Guid khasraId, string? rawKhasraText, decimal? recordedArea, string? rawAreaText, string? areaUnit, string? remarks, CancellationToken ct)
    {
        var khata = await db.Khatas.Include(x => x.KhatauniRecord).SingleOrDefaultAsync(x => x.Id == khataId, ct) ?? throw new OwnershipWorkflowException("Khata was not found.", 404);
        if (recordedArea is <= 0) throw new OwnershipWorkflowException("Recorded area must be positive when provided.");
        if (!await db.Khasras.AnyAsync(x => x.Id == khasraId && x.VillageId == khata.KhatauniRecord.VillageId, ct)) throw new OwnershipWorkflowException("The selected Khasra does not belong to this Khatauni village.");
        var existing = await db.KhataKhasras.SingleOrDefaultAsync(x => x.KhataId == khataId && x.KhasraId == khasraId, ct);
        if (existing is not null) return existing;
        var link = new KhataKhasra { KhataId = khataId, KhasraId = khasraId, RawKhasraText = Clean(rawKhasraText), RecordedArea = recordedArea, RawAreaText = Clean(rawAreaText), AreaUnit = Clean(areaUnit), Remarks = Clean(remarks) };
        db.KhataKhasras.Add(link); await db.SaveChangesAsync(ct); return link;
    }

    public async Task<KhataPartyShare> AddShareAsync(Guid khataId, Guid partyId, string? rawShareText, int? numerator, int? denominator, string? remarks, RevenueRecordVerificationStatus verificationStatus, CancellationToken ct)
    {
        if ((numerator is null) != (denominator is null) || numerator is < 0 || denominator is <= 0) throw new OwnershipWorkflowException("Structured share requires a non-negative numerator and positive denominator together.");
        if (!await db.Khatas.AnyAsync(x => x.Id == khataId, ct)) throw new OwnershipWorkflowException("Khata was not found.", 404);
        if (!await db.Parties.AnyAsync(x => x.Id == partyId, ct)) throw new OwnershipWorkflowException("Party was not found.", 404);
        var existing = await db.KhataPartyShares.SingleOrDefaultAsync(x => x.KhataId == khataId && x.PartyId == partyId, ct);
        if (existing is not null) return existing;
        var share = new KhataPartyShare { KhataId = khataId, PartyId = partyId, RawShareText = Clean(rawShareText), ShareNumerator = numerator, ShareDenominator = denominator, Remarks = Clean(remarks), VerificationStatus = verificationStatus };
        db.KhataPartyShares.Add(share); await db.SaveChangesAsync(ct); return share;
    }

    public async Task<RecordedOwnershipResult> GetRecordedOwnershipAsync(Guid khasraId, DateOnly? asOfDate, CancellationToken ct)
    {
        var candidates = await db.KhataKhasras.AsNoTracking().Where(x => x.KhasraId == khasraId && x.Khata.KhatauniRecord.VerificationStatus == RevenueRecordVerificationStatus.Verified)
            .Select(x => new { x.KhataId, x.Khata.KhatauniRecordId, x.Khata.KhatauniRecord.AsOfDate, x.Khata.KhatauniRecord.EffectiveFrom, x.Khata.KhatauniRecord.EffectiveTo })
            .ToListAsync(ct);
        if (asOfDate is not null) candidates = candidates.Where(x => (x.EffectiveFrom is null || x.EffectiveFrom <= asOfDate) && (x.EffectiveTo is null || x.EffectiveTo >= asOfDate) && (x.AsOfDate is null || x.AsOfDate <= asOfDate)).ToList();
        if (candidates.Count == 0) return new(false, false, "No verified recorded ownership is available for this context.", null, null, []);
        var dated = candidates.Where(x => x.AsOfDate is not null).OrderByDescending(x => x.AsOfDate).ToList();
        if (dated.Count == 0 || (dated.Count > 1 && dated[0].AsOfDate == dated[1].AsOfDate)) return new(false, true, "Latest recorded ownership cannot be determined automatically.", null, null, []);
        var selected = dated[0];
        var owners = await db.KhataPartyShares.AsNoTracking().Where(x => x.KhataId == selected.KhataId).OrderBy(x => x.Party.DisplayName).Select(x => new RecordedOwner(x.PartyId, x.Party.DisplayName, x.RawShareText, x.ShareNumerator, x.ShareDenominator)).ToListAsync(ct);
        return new(true, false, null, selected.KhatauniRecordId, selected.KhataId, owners);
    }

    public async Task VerifyKhatauniAsync(Guid id, int expectedVersion, CancellationToken ct)
    {
        var record = await db.KhatauniRecords.Include(x => x.Khatas).ThenInclude(x => x.PartyShares).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new OwnershipWorkflowException("Khatauni record was not found.", 404);
        if (record.Version != expectedVersion) throw new OwnershipWorkflowException("This Khatauni record was changed by another user. Reload before verifying.", 409);
        foreach (var khata in record.Khatas)
        {
            if (khata.PartyShares.Count == 0 || khata.PartyShares.Any(x => x.VerificationStatus != RevenueRecordVerificationStatus.Verified)) throw new OwnershipWorkflowException($"Khata {khata.KhataNumber} has unresolved recorded ownership.");
            var validation = ValidateShares(khata.PartyShares);
            if (!validation.IsComplete || !validation.IsValid) throw new OwnershipWorkflowException($"Khata {khata.KhataNumber}: {validation.Message}");
        }
        record.VerificationStatus = RevenueRecordVerificationStatus.Verified; record.Version++; await db.SaveChangesAsync(ct);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static int Gcd(int a, int b) { a = Math.Abs(a); b = Math.Abs(b); while (b != 0) (a, b) = (b, a % b); return a == 0 ? 1 : a; }
    private static int Lcm(int a, int b) => checked(a / Gcd(a, b) * b);
    private static (int Numerator, int Denominator) Reduce(int n, int d) { var gcd = Gcd(n, d); return (n / gcd, d / gcd); }
}
