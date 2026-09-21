using LAC.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LAC.Infrastructure;

public static class SeedData
{
    public static readonly Guid BootstrapAdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SystemAdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static Task SeedAsync(LacDbContext db, CancellationToken ct = default) => SeedAsync(db, null, null, ct);

    public static async Task SeedAsync(LacDbContext db, IConfiguration? configuration, ILogger? logger = null, CancellationToken ct = default)
    {
        await EnsureIdentityFoundationAsync(db, configuration, logger, ct);

        var bootstrapComplete = await db.Districts.AsNoTracking().Where(x => x.Name == "South West Delhi").Select(x => new
        {
            SubDivisionCount = x.SubDivisions.Count,
            VillageCount = x.SubDivisions.SelectMany(s => s.Villages).Count(),
            HasDemoOwnership = db.KhatauniRecords.Any(r => r.ReferenceNumber == "DEMO-KHATAUNI-2024")
        }).SingleOrDefaultAsync(ct);

        if (bootstrapComplete is { SubDivisionCount: 4, VillageCount: 76, HasDemoOwnership: true }) return;

        if (!await db.Districts.AnyAsync(ct))
        {
            var d = new District { Name = "South West Delhi" };
            var s = new SubDivision { District = d, Name = "Matiala" };
            var v = new Village { SubDivision = s, Name = "GALIB PUR" };
            var p = new AcquisitionProject { Name = "Demo Corridor Acquisition", RequiringAgency = "Demo Requiring Agency", ActRegime = "Land Acquisition Act (demo)" };
            var k1 = new Khasra { Village = v, DisplayNumber = "22//1", NormalizedNumber = KhasraNumber.Normalize("22//1"), TotalArea = 4m, AreaUnit = "Bigha" };
            var k2 = new Khasra { Village = v, DisplayNumber = "22//2", NormalizedNumber = KhasraNumber.Normalize("22//2"), TotalArea = 3.5m, AreaUnit = "Bigha" };
            var k3 = new Khasra { Village = v, DisplayNumber = "22//1/6", NormalizedNumber = KhasraNumber.Normalize("22//1/6"), TotalArea = 1.25m, AreaUnit = "Bigha" };
            var n4 = new Notification { AcquisitionProject = p, SectionType = "4", NotificationNumber = "DEMO-S4-2026-001", NotificationDate = new DateOnly(2026, 1, 12) };
            var n6 = new Notification { AcquisitionProject = p, SectionType = "6", NotificationNumber = "DEMO-S6-2026-001", NotificationDate = new DateOnly(2026, 3, 4) };
            var a = new Award { AcquisitionProject = p, AwardNumber = "DEMO-AWARD-01", AwardDate = new DateOnly(2026, 6, 1), AwardType = "Demo", Status = "Published" };
            var lr = new VillageLR { Village = v, RegisterReference = "DEMO-LR-GAL-01" };
            db.AddRange(d, s, v, p, k1, k2, k3, n4, n6, a, lr,
                new NotificationKhasra { Notification = n4, Khasra = k2, NotifiedArea = 2m, AreaUnit = "Bigha" },
                new NotificationKhasra { Notification = n6, Khasra = k2, NotifiedArea = 2m, AreaUnit = "Bigha" },
                new AwardKhasra { Award = a, Khasra = k2, AcquiredArea = 2m, AreaUnit = "Bigha", AcquisitionStatus = "Acquired" },
                new AwardKhasra { Award = a, Khasra = k3, AcquiredArea = 1.25m, AreaUnit = "Bigha", AcquisitionStatus = "Acquired" },
                new LREntry { VillageLR = lr, RowNumber = 1, RawKhasraText = "22//2 min", Khasra = k2, RawAreaText = "2 bigha", ParsedArea = 2m, AreaUnit = "Bigha", Section4NotificationId = n4.Id, Section6NotificationId = n6.Id, AwardId = a.Id, RawRemarks = "Dummy training data", VerificationStatus = VerificationStatus.Verified });
            await db.SaveChangesAsync(ct);
        }
        await EnsureSouthWestVillageDirectoryAsync(db, ct);
        if (await db.KhatauniRecords.AnyAsync(x => x.ReferenceNumber == "DEMO-KHATAUNI-2024", ct)) return;
        var village = await db.Villages.SingleAsync(x => x.Name == "GALIB PUR", ct);
        var k2Existing = await db.Khasras.SingleAsync(x => x.VillageId == village.Id && x.NormalizedNumber == KhasraNumber.Normalize("22//2"), ct);
        var k3Existing = await db.Khasras.SingleAsync(x => x.VillageId == village.Id && x.NormalizedNumber == KhasraNumber.Normalize("22//1/6"), ct);
        var record = new KhatauniRecord { Village = village, ReferenceNumber = "DEMO-KHATAUNI-2024", RecordYearText = "2024", AsOfDate = new DateOnly(2024, 1, 1), EffectiveFrom = new DateOnly(2024, 1, 1), VerificationStatus = RevenueRecordVerificationStatus.Verified, Remarks = "Fictional demonstration revenue record only." };
        var khata = new Khata { KhatauniRecord = record, KhataNumber = "DEMO-KHATA-145", RawKhataNumber = "145" };
        var ram = new Party { DisplayName = "Demo Ram Singh", PartyType = PartyType.Individual, FatherOrSpouseName = "Demo Parent" };
        var shyam = new Party { DisplayName = "Demo Shyam Singh", PartyType = PartyType.Individual, FatherOrSpouseName = "Demo Parent" };
        db.AddRange(record, khata, ram, shyam,
            new KhataKhasra { Khata = khata, Khasra = k2Existing, RawKhasraText = "22//2", RecordedArea = 3.5m, RawAreaText = "3.5 bigha", AreaUnit = "Bigha" },
            new KhataKhasra { Khata = khata, Khasra = k3Existing, RawKhasraText = "22//1/6", RecordedArea = 1.25m, RawAreaText = "1.25 bigha", AreaUnit = "Bigha" },
            new KhataPartyShare { Khata = khata, Party = ram, RawShareText = "1/2", ShareNumerator = 1, ShareDenominator = 2, VerificationStatus = RevenueRecordVerificationStatus.Verified },
            new KhataPartyShare { Khata = khata, Party = shyam, RawShareText = "1/2", ShareNumerator = 1, ShareDenominator = 2, VerificationStatus = RevenueRecordVerificationStatus.Verified });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureIdentityFoundationAsync(LacDbContext db, IConfiguration? configuration, ILogger? logger, CancellationToken ct)
    {
        // 1. Initial 7 Designations
        var initialDesignations = new (string Code, string Name, int Order)[]
        {
            ("ADM", "Additional District Magistrate", 1),
            ("SO", "Section Officer", 2),
            ("NT", "Naib Tehsildar", 3),
            ("AAO", "Assistant Accounts Officer", 4),
            ("PATWARI", "Patwari", 5),
            ("DEO", "Data Entry Operator", 6),
            ("RECORD_ROOM", "Record Room In-charge", 7)
        };

        foreach (var (code, name, order) in initialDesignations)
        {
            if (!await db.Designations.AnyAsync(d => d.Code == code, ct))
            {
                db.Designations.Add(new Designation { Code = code, Name = name, DisplayOrder = order, IsActive = true });
            }
        }
        await db.SaveChangesAsync(ct);

        // 2. Initial 10 Workstreams
        var initialWorkstreams = new (string Code, string Name, string Description)[]
        {
            ("DAK_CORRESPONDENCE", "Dak & Correspondence", "Receipt, dispatch, and diarizing of official correspondence"),
            ("LAND_ACQUISITION", "Land Acquisition", "Core land acquisition projects, surveys, and notices"),
            ("AWARD", "Award", "Award inquiry, draft preparation, and pronouncement"),
            ("LAND_RECORDS", "Land Records", "Village land registers, khasras, and revenue records"),
            ("POSSESSION", "Possession", "Possession proceedings, notices, and possession memo"),
            ("ACCOUNTS_COMPENSATION", "Accounts & Compensation", "Apportionment, payment vouchers, and compensation statements"),
            ("COURT_REFERENCES", "Court References", "References under section 18/30/64 and legal proceedings"),
            ("RTI", "Right to Information", "RTI applications, appeals, and responses"),
            ("RECORD_ROOM", "Record Room", "Physical and digital record preservation and inspection"),
            ("DRAFTING_NOTING", "Drafting & Noting", "Matter drafting, letters, notings, and approvals")
        };

        foreach (var (code, name, desc) in initialWorkstreams)
        {
            if (!await db.Workstreams.AnyAsync(w => w.Code == code, ct))
            {
                db.Workstreams.Add(new Workstream { Code = code, Name = name, Description = desc, IsActive = true });
            }
        }
        await db.SaveChangesAsync(ct);

        // 3. Permissions catalog
        foreach (var perm in PermissionCodes.All)
        {
            var existing = await db.Permissions.FirstOrDefaultAsync(p => p.Code == perm.Code, ct);
            if (existing is null)
            {
                db.Permissions.Add(new Permission
                {
                    Code = perm.Code,
                    Name = perm.Name,
                    Description = perm.Description,
                    Category = perm.Category
                });
            }
        }
        await db.SaveChangesAsync(ct);

        // 4. SYSTEM_ADMIN Role with ScopeMode.All on all permissions
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "SYSTEM_ADMIN", ct);
        if (adminRole is null)
        {
            adminRole = new Role
            {
                Id = SystemAdminRoleId,
                Code = "SYSTEM_ADMIN",
                Name = "System Administrator",
                Description = "Full administrative access to all modules and configurations",
                IsSystemRole = true,
                IsActive = true
            };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync(ct);
        }

        var allPermissions = await db.Permissions.ToListAsync(ct);
        var existingRolePerms = await db.RolePermissions.Where(rp => rp.RoleId == adminRole.Id).ToListAsync(ct);
        foreach (var perm in allPermissions)
        {
            var rp = existingRolePerms.FirstOrDefault(x => x.PermissionId == perm.Id);
            if (rp is null)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = adminRole.Id,
                    PermissionId = perm.Id,
                    ScopeMode = ScopeMode.All
                });
            }
            else if (rp.ScopeMode != ScopeMode.All)
            {
                rp.ScopeMode = ScopeMode.All;
            }
        }
        await db.SaveChangesAsync(ct);

        await MigrateCourtPermissionsAsync(db, ct);

        // 5. Bootstrap Admin User
        if (!await db.AppUsers.AnyAsync(ct))
        {
            var username = configuration?["BootstrapAdmin:Username"]?.Trim();
            var password = configuration?["BootstrapAdmin:Password"];
            var displayName = configuration?["BootstrapAdmin:DisplayName"]?.Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                logger?.LogWarning("BootstrapAdmin credentials are not configured or blank. No initial administrator account was created. AppUsers table remains empty. Set BootstrapAdmin:Username and BootstrapAdmin:Password via environment variables or configuration.");
                return;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "System Administrator";
            }

            var hasher = new PasswordHasher<AppUser>();
            var admDesignation = await db.Designations.FirstOrDefaultAsync(d => d.Code == "ADM", ct);
            var primaryWorkstream = await db.Workstreams.FirstOrDefaultAsync(w => w.Code == "LAND_ACQUISITION", ct);

            var user = new AppUser
            {
                Id = BootstrapAdminId,
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                DisplayName = displayName,
                DesignationId = admDesignation?.Id,
                IsActive = true,
                PasswordChangedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);

            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id,
                AssignedAt = DateTimeOffset.UtcNow
            });

            if (primaryWorkstream is not null)
            {
                db.UserWorkstreamMemberships.Add(new UserWorkstreamMembership
                {
                    UserId = user.Id,
                    WorkstreamId = primaryWorkstream.Id,
                    IsPrimary = true,
                    IsActive = true,
                    AssignedAt = DateTimeOffset.UtcNow
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureSouthWestVillageDirectoryAsync(LacDbContext db, CancellationToken ct)
    {
        var district = await db.Districts.SingleAsync(x => x.Name == "South West Delhi", ct);
        var legacy = await db.SubDivisions.SingleOrDefaultAsync(x => x.DistrictId == district.Id && x.Name == "Demo Sub-Division", ct);
        var matiala = await db.SubDivisions.SingleOrDefaultAsync(x => x.DistrictId == district.Id && x.Name == "Matiala", ct);
        if (legacy is not null && matiala is not null)
        {
            await db.Villages.Where(x => x.SubDivisionId == legacy.Id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SubDivisionId, matiala.Id), ct);
            await db.SubDivisions.Where(x => x.Id == legacy.Id).ExecuteDeleteAsync(ct);
        }
        else if (legacy is not null)
        {
            legacy.Name = "Matiala";
            await db.SaveChangesAsync(ct);
        }
        var legacyGalib = await db.Villages.SingleOrDefaultAsync(x => x.Name == "Galibpur" || x.Name == "Galib Pur", ct);
        if (legacyGalib is not null)
        {
            legacyGalib.Name = "GALIB PUR";
            await db.SaveChangesAsync(ct);
        }
        var villagesBySubdivision = new Dictionary<string, string[]>
        {
            ["Matiala"] = ["Ambar Hai", "Sarangpur", "Guman Hera", "Darya Pur Khurd", "Rawta", "Devrala", "Pindwala Kalan", "Pindwala Khurd", "Khar Khari Jatmal", "Khar Khari Rond", "Jhatikra", "Ragho Pur", "Nanak Heri", "Badu Sarai", "Shikar Pur", "Asalat Pur Khawad", "Jain Pur", "Hasanpur", "Daulat Pur", "Rewla Khan Pur", "Paprawat", "Goela Khurd", "Taj Pur Khurd", "Quiba Pur", "Chhawla", "Kangan Heri", "Jhul Jhuli", "Kakrola", "Khera Dabar", "Dindar Pur", "Luhar Heri", "Sahupura", "Sher Pur Dairy", "Galib Pur", "Khar Khari Nahar", "Matiala", "Nangli Sakrawati", "Pochan Pur"],
            ["Bijwasan"] = ["Bharthal", "Bijwasan", "Dhool Siras", "Rangpuri", "Salah Pur", "Bagdola", "Bamnoli", "Kapas Hera", "Mahipalpur", "Nangal Dewat", "Sahbad Mohd", "Samalka", "Toganpur"],
            ["Najafgarh"] = ["Roshan Pura", "Dichaon Kalan", "Jharoda Kalan", "Surakh Pur", "Mitraun", "Khaira", "Surhera", "Kair", "Ujwa", "Jafar Pur Kalan", "Malik Pur Zer N Garh", "Mundhela Kalan", "Mundhela Khurd", "Samas Pur Khalsalssa Pur", "Issa Pur", "Qazi Pur", "Baqar Garh", "Dhansa", "Najafgarh", "Haibat Pur", "Masuda Bad"],
            ["Dwarka"] = ["Sagarpur", "Palam", "Mirza Pur", "Nasir Pur"]
        };
        var subdivisions = await db.SubDivisions.Where(x => x.DistrictId == district.Id).ToListAsync(ct);
        foreach (var name in villagesBySubdivision.Keys)
        {
            if (subdivisions.All(x => x.Name != name))
            {
                var subdivision = new SubDivision { DistrictId = district.Id, Name = name };
                db.SubDivisions.Add(subdivision);
                subdivisions.Add(subdivision);
            }
        }
        await db.SaveChangesAsync(ct);
        var subdivisionIds = subdivisions.Select(x => x.Id).ToList();
        var existingVillageKeys = (await db.Villages.Where(x => subdivisionIds.Contains(x.SubDivisionId)).Select(x => new { x.SubDivisionId, x.Name }).ToListAsync(ct)).Select(x => $"{x.SubDivisionId}:{x.Name}").ToHashSet();
        foreach (var (name, villages) in villagesBySubdivision)
        {
            var subdivision = subdivisions.Single(x => x.Name == name);
            foreach (var villageName in villages)
            {
                var officialName = villageName.ToUpperInvariant();
                if (existingVillageKeys.Add($"{subdivision.Id}:{officialName}")) db.Villages.Add(new Village { SubDivisionId = subdivision.Id, Name = officialName });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task MigrateCourtPermissionsAsync(LacDbContext db, CancellationToken ct = default)
    {
        var awardView = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.AwardView, ct);
        var awardEdit = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.AwardEdit, ct);
        var courtView = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.CourtView, ct);
        var courtCreate = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.CourtCreate, ct);
        var courtEdit = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.CourtEdit, ct);
        var courtProceedingManage = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.CourtProceedingManage, ct);
        var courtDocumentManage = await db.Permissions.FirstOrDefaultAsync(p => p.Code == PermissionCodes.CourtDocumentManage, ct);

        if (courtView is null) return;

        var eligibleScopes = new[] { ScopeMode.All, ScopeMode.Workstream };

        if (awardView is not null)
        {
            var awardViewRolePerms = await db.RolePermissions
                .Where(rp => rp.PermissionId == awardView.Id && eligibleScopes.Contains(rp.ScopeMode))
                .ToListAsync(ct);

            foreach (var rp in awardViewRolePerms)
            {
                var exists = await db.RolePermissions.AnyAsync(x => x.RoleId == rp.RoleId && x.PermissionId == courtView.Id, ct);
                if (!exists)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = rp.RoleId,
                        PermissionId = courtView.Id,
                        ScopeMode = rp.ScopeMode
                    });
                }
            }
        }

        if (awardEdit is not null)
        {
            var awardEditRolePerms = await db.RolePermissions
                .Where(rp => rp.PermissionId == awardEdit.Id && eligibleScopes.Contains(rp.ScopeMode))
                .ToListAsync(ct);

            var targetPerms = new[] { courtCreate, courtEdit, courtProceedingManage, courtDocumentManage }
                .Where(p => p is not null).Cast<Permission>().ToList();

            foreach (var rp in awardEditRolePerms)
            {
                foreach (var target in targetPerms)
                {
                    var exists = await db.RolePermissions.AnyAsync(x => x.RoleId == rp.RoleId && x.PermissionId == target.Id, ct);
                    if (!exists)
                    {
                        db.RolePermissions.Add(new RolePermission
                        {
                            RoleId = rp.RoleId,
                            PermissionId = target.Id,
                            ScopeMode = rp.ScopeMode
                        });
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

