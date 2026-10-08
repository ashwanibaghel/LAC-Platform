using LAC.Domain;
using Microsoft.EntityFrameworkCore;

namespace LAC.Infrastructure;

public static class OfficeSessionSecurity
{
    // State is saved atomically with the access change. Helper browser claims cannot outlive parent changes.
    public static async Task InvalidateAsync(LacDbContext db, AppUser user, CancellationToken ct)
    {
        user.SessionVersion = Guid.NewGuid();
        user.OfficeRevision++;
        foreach (var child in await db.AppUsers.Where(u => u.SupervisingOfficerId == user.Id).ToListAsync(ct))
        {
            child.SessionVersion = Guid.NewGuid();
            child.OfficeRevision++;
        }
    }
}
