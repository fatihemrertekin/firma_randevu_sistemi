using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class StaffAccountEndpoints
{
    public sealed record StaffAccount(Guid Id, string Email, bool IsActive, string Version);
    public sealed record StaffPage(StaffAccount[] Items, int Page, bool HasMore);
    public sealed record DeactivateRequest(string Version);

    public static void MapStaffAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/staff-accounts").RequireAuthorization("Owner").RequireRateLimiting("login");
        accounts.MapGet("/", ListAsync);
        accounts.MapPost("/{id:guid}/deactivate", DeactivateAsync);
    }

    private static IQueryable<AppUser> StaffOnly(AppDbContext db) => db.Users.Where(user =>
        db.UserRoles.Any(link => link.UserId == user.Id && db.Roles.Any(role => role.Id == link.RoleId && role.NormalizedName == "STAFF")) &&
        !db.UserRoles.Any(link => link.UserId == user.Id && db.Roles.Any(role => role.Id == link.RoleId && role.NormalizedName == "OWNER")));

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (page is < 1 or > 10000 || pageSize is < 1 or > 50)
            return Results.Problem(statusCode: 400, title: "Geçerli sayfa ve 1–50 arası sayfa boyutu gerekli.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var rows = await StaffOnly(db).AsNoTracking().OrderBy(user => user.Email).ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(user => new StaffAccount(user.Id, user.Email ?? "", user.IsActive, user.ConcurrencyStamp ?? ""))
            .ToArrayAsync(timeout.Token);
        return Results.Ok(new StaffPage(rows.Take(pageSize).ToArray(), page, rows.Length > pageSize));
    }

    private static async Task<IResult> DeactivateAsync(Guid id, DeactivateRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (string.IsNullOrWhiteSpace(request.Version) || request.Version.Length > 128)
            return Results.Problem(statusCode: 400, title: "Hesap sürümü gerekli.");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
            return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        db.ChangeTracker.Clear();
        // Beklerken iptal edilen Owner oturumu işlem yapamasın; kilit sırası Owner → Staff.
        var owner = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (owner is null || !owner.IsActive || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
            !context.User.HasClaim("amr", "mfa") || await users.IsLockedOutAsync(owner) ||
            owner.SecurityStamp != context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value)
            return Results.Unauthorized();
        // Owner ve çift rol hedeflerini ikinci hesap kilidinden önce reddet.
        if (!await StaffOnly(db).AnyAsync(user => user.Id == id, timeout.Token)) return Results.NotFound();
        var staff = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (staff is null || !await users.IsInRoleAsync(staff, "Staff") || await users.IsInRoleAsync(staff, "Owner"))
            return Results.NotFound();
        if (!staff.IsActive) return Results.NoContent();
        if (staff.ConcurrencyStamp != request.Version)
            return Results.Problem(statusCode: 409, title: "Hesap değişti. Güncel listeyi yükleyin.");
        staff.IsActive = false;
        if (!(await users.UpdateSecurityStampAsync(staff)).Succeeded)
            throw new InvalidOperationException("Staff hesabı pasifleştirilemedi.");
        db.StaffDeactivationAudits.Add(new StaffDeactivationAudit
        {
            Id = Guid.NewGuid(),
            StaffId = staff.Id,
            ActorId = ownerId,
            OccurredAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.NoContent();
    }
}
