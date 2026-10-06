using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class StaffAccountEndpoints
{
    public sealed record StaffAccount(Guid Id, string Email, bool IsActive, string Version);
    public sealed record StaffPage(StaffAccount[] Items, int Page, bool HasMore, int PageSize, int TotalCount);
    public sealed record StateChangeRequest(string Version);

    public static void MapStaffAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/staff-accounts").RequireAuthorization("Owner");
        accounts.MapGet("/", ListAsync).RequireRateLimiting("staff-management");
        accounts.MapPost("/{id:guid}/deactivate", DeactivateAsync).RequireRateLimiting("login");
        accounts.MapPost("/{id:guid}/activate", ActivateAsync).RequireRateLimiting("login");
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
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var query = StaffOnly(db).AsNoTracking();
        var totalCount = await query.CountAsync(timeout.Token);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize)));
        var rows = await query.OrderBy(user => user.Email).ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(user => new StaffAccount(user.Id, user.Email ?? "", user.IsActive, user.ConcurrencyStamp ?? ""))
            .ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new StaffPage(rows.Take(pageSize).ToArray(), page, rows.Length > pageSize, pageSize, totalCount));
    }

    private static Task<IResult> DeactivateAsync(Guid id, StateChangeRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeActiveAsync(id, request, false, context, antiforgery, db, users, clock);

    private static Task<IResult> ActivateAsync(Guid id, StateChangeRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeActiveAsync(id, request, true, context, antiforgery, db, users, clock);

    private static async Task<IResult> ChangeActiveAsync(Guid id, StateChangeRequest request, bool active, HttpContext context,
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
        if (staff.IsActive == active) return Results.NoContent();
        if (staff.ConcurrencyStamp != request.Version)
            return Results.Problem(statusCode: 409, title: "Hesap değişti. Güncel listeyi yükleyin.");
        staff.IsActive = active;
        if (!(await users.UpdateSecurityStampAsync(staff)).Succeeded)
            throw new InvalidOperationException("Staff hesabının erişim durumu değiştirilemedi.");
        if (active)
            db.StaffActivationAudits.Add(new StaffActivationAudit
            {
                Id = Guid.NewGuid(),
                StaffId = staff.Id,
                ActorId = ownerId,
                OccurredAt = clock.GetUtcNow()
            });
        else
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
