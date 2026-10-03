using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

internal static class OwnerMutationAuthorization
{
    // Çağıran endpoint transaction açar. Kilit beklerken iptal edilen oturum yazamasın.
    internal static async Task<AppUser?> LockAsync(HttpContext context, AppDbContext db,
        UserManager<AppUser> users, CancellationToken token)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        db.ChangeTracker.Clear();
        var owner = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(token);
        return owner is not null && owner.IsActive && owner.TwoFactorEnabled && context.User.HasClaim("amr", "mfa") &&
            await users.IsInRoleAsync(owner, "Owner") && !await users.IsLockedOutAsync(owner) &&
            owner.SecurityStamp == context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value ? owner : null;
    }
}
