using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class StaffInvitationEndpoints
{
    public sealed record IssueRequest(string Email, bool VerifiedRecipient);
    public sealed record AcceptRequest(string Email, string Token, string Password, string ConfirmPassword);
    public sealed record IssuedResponse(Guid Id, string Token, DateTimeOffset ExpiresAt);

    public static void MapStaffInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var invitations = app.MapGroup("/api/staff-invitations");
        invitations.MapGet("/", async (HttpContext context, AppDbContext db, IConfiguration config, TimeProvider clock) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var now = clock.GetUtcNow();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            return Results.Ok(await db.StaffInvitations.AsNoTracking()
                .Where(entry => entry.InstanceId == config["Auth:InstanceId"] && entry.AcceptedAt == null &&
                    entry.RevokedAt == null && entry.ExpiresAt > now)
                .OrderBy(entry => entry.ExpiresAt).Take(20)
                .Select(entry => new { entry.Id, entry.Email, entry.ExpiresAt }).ToListAsync(timeout.Token));
        }).RequireAuthorization("Owner");
        invitations.MapPost("/", IssueAsync).RequireAuthorization("Owner").RequireRateLimiting("login");
        invitations.MapPost("/{id:guid}/revoke", RevokeAsync).RequireAuthorization("Owner").RequireRateLimiting("login");
        invitations.MapPost("/accept", AcceptAsync).AllowAnonymous().RequireRateLimiting("login");
    }

    private static bool ValidEmail(string? email) => email is { Length: > 0 and <= 256 } &&
        new EmailAddressAttribute().IsValid(email.Trim());
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static IResult InvalidInvitation() => Results.Problem(statusCode: 400, title: "Davet kodu veya e-posta geçersiz; davet kullanılmış, iptal edilmiş ya da süresi dolmuş olabilir.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Bu adres için hesap veya geçerli davet var. Gerekirse bekleyen daveti iptal edin.");
    private static StaffInvitationAudit Audit(StaffInvitation invitation, Guid actor, string kind, TimeProvider clock) =>
        new() { Id = Guid.NewGuid(), InvitationId = invitation.Id, ActorId = actor, Kind = kind, OccurredAt = clock.GetUtcNow() };

    private static async Task<AppUser?> LockOwnerAsync(Guid id, HttpContext context, AppDbContext db,
        UserManager<AppUser> users, CancellationToken cancellationToken)
    {
        // Authentication may have tracked an older account before waiting for this lock.
        db.ChangeTracker.Clear();
        var owner = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        return owner is not null && owner.TwoFactorEnabled && await users.IsInRoleAsync(owner, "Owner") &&
            !await users.IsLockedOutAsync(owner) && owner.SecurityStamp ==
            context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value ? owner : null;
    }

    private static async Task<IResult> IssueAsync(IssueRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, IConfiguration config,
        TimeProvider clock, PartitionedRateLimiter<Guid> accountLimiter)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (!request.VerifiedRecipient || !ValidEmail(request.Email)) return Results.Problem(statusCode: 400, title: "Geçerli e-posta ve alıcının doğrulandığına ilişkin onay gerekli.");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId)) return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var lease = await accountLimiter.AcquireAsync(ownerId, cancellationToken: timeout.Token);
        if (!lease.IsAcquired) return Results.StatusCode(429);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        if (await LockOwnerAsync(ownerId, context, db, users, timeout.Token) is null) return Results.Unauthorized();
        var email = request.Email.Trim();
        var normalized = users.NormalizeEmail(email) ?? throw new InvalidOperationException("E-posta normalleştirilemedi.");
        var instance = config["Auth:InstanceId"] ?? throw new InvalidOperationException("Firma kimliği gerekli.");
        var invitation = await db.StaffInvitations.FromSqlInterpolated(
            $"SELECT * FROM \"StaffInvitations\" WHERE \"InstanceId\" = {instance} AND \"NormalizedEmail\" = {normalized} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        var now = clock.GetUtcNow();
        if (await users.FindByEmailAsync(email) is not null || invitation?.AcceptedAt is not null ||
            invitation is { RevokedAt: null } && invitation.ExpiresAt > now) return Conflict();
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        if (invitation is null)
        {
            invitation = new StaffInvitation
            {
                Id = Guid.NewGuid(),
                InstanceId = instance,
                Email = email,
                NormalizedEmail = normalized,
                TokenHash = Hash(token)
            };
            db.StaffInvitations.Add(invitation);
        }
        invitation.Email = email;
        invitation.TokenHash = Hash(token);
        invitation.IssuedById = ownerId;
        invitation.ExpiresAt = now.AddHours(24);
        invitation.RevokedAt = null;
        db.StaffInvitationAudits.Add(Audit(invitation, ownerId, "Issued", clock));
        try
        {
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict();
        }
        return Results.Ok(new IssuedResponse(invitation.Id, token, invitation.ExpiresAt));
    }

    private static async Task<IResult> RevokeAsync(Guid id, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, IConfiguration config, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId)) return Results.Unauthorized();
        if (await LockOwnerAsync(ownerId, context, db, users, timeout.Token) is null) return Results.Unauthorized();
        var instance = config["Auth:InstanceId"];
        var invitation = await db.StaffInvitations.FromSqlInterpolated(
            $"SELECT * FROM \"StaffInvitations\" WHERE \"Id\" = {id} AND \"InstanceId\" = {instance} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (invitation is null) return Results.NotFound();
        if (invitation.AcceptedAt is not null) return Results.Problem(statusCode: 409, title: "Davet zaten kullanılmış; bu işlem hesabı kapatmaz.");
        if (invitation.RevokedAt is not null) return Results.NoContent();
        invitation.RevokedAt = clock.GetUtcNow();
        db.StaffInvitationAudits.Add(Audit(invitation, ownerId, "Revoked", clock));
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.NoContent();
    }

    private static async Task<IResult> AcceptAsync(AcceptRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, IConfiguration config,
        TimeProvider clock, PartitionedRateLimiter<Guid> accountLimiter)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (context.User.Identity?.IsAuthenticated == true) return Results.Problem(statusCode: 409, title: "Daveti kabul etmek için mevcut hesaptan çıkış yapın.");
        if (!ValidEmail(request.Email) || request.Token is not { Length: 43 } ||
            !request.Token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) return InvalidInvitation();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 || request.Password != request.ConfirmPassword)
            return Results.Problem(statusCode: 400, title: "Parola ve aynı tekrarı gerekli.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var hash = Hash(request.Token);
        var instance = config["Auth:InstanceId"];
        var id = await db.StaffInvitations.AsNoTracking().Where(entry => entry.InstanceId == instance && entry.TokenHash == hash)
            .Select(entry => (Guid?)entry.Id).SingleOrDefaultAsync(timeout.Token);
        if (id is null) return InvalidInvitation();
        using var lease = await accountLimiter.AcquireAsync(id.Value, cancellationToken: timeout.Token);
        if (!lease.IsAcquired) return Results.StatusCode(429);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var invitation = await db.StaffInvitations.FromSqlInterpolated(
            $"SELECT * FROM \"StaffInvitations\" WHERE \"Id\" = {id.Value} FOR UPDATE").SingleAsync(timeout.Token);
        if (invitation.TokenHash != hash || invitation.InstanceId != instance || invitation.AcceptedAt is not null ||
            invitation.RevokedAt is not null || clock.GetUtcNow() >= invitation.ExpiresAt ||
            invitation.NormalizedEmail != users.NormalizeEmail(request.Email.Trim())) return InvalidInvitation();
        // Owner verifies the recipient/address before private delivery. Possession of that
        // address-bound invitation confirms this manual flow; it is not proof of email delivery.
        var user = new AppUser { UserName = invitation.Email, Email = invitation.Email, EmailConfirmed = true };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return Results.Problem(statusCode: 400, title: "Hesap oluşturulamadı; parola kurallarını ve daveti kontrol edin.");
        var assigned = await users.AddToRoleAsync(user, "Staff");
        if (!assigned.Succeeded) throw new InvalidOperationException("Staff rolü atanamadı.");
        invitation.AcceptedAt = clock.GetUtcNow();
        invitation.AcceptedUserId = user.Id;
        db.StaffInvitationAudits.Add(Audit(invitation, user.Id, "Accepted", clock));
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.NoContent();
    }
}
