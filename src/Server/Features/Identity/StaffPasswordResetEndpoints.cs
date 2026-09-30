using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class StaffPasswordResetEndpoints
{
    internal const string EnvelopePurpose = "StaffPasswordReset.Grant.v1";
    public sealed record IssueRequest(string Email, bool VerifiedRecipient);
    public sealed record IssuedResponse(string Token, DateTimeOffset ExpiresAt);

    public static void MapStaffPasswordResetEndpoints(this IEndpointRouteBuilder app)
    {
        var resets = app.MapGroup("/api/staff-password-resets");
        resets.MapPost("/", IssueAsync).RequireAuthorization("Owner").RequireRateLimiting("login");
        resets.MapPost("/complete", CompleteAsync).AllowAnonymous().RequireRateLimiting("login");
    }

    private static IResult InvalidToken() => Results.Problem(statusCode: 400,
        title: "Sıfırlama kodu geçersiz veya süresi dolmuş.");

    private static StaffPasswordResetAudit Audit(Guid grantId, Guid staffId, Guid actorId,
        string instance, string kind, DateTimeOffset now, DateTimeOffset expiresAt) => new()
        {
            Id = Guid.NewGuid(),
            GrantId = grantId,
            StaffId = staffId,
            ActorId = actorId,
            InstanceId = instance,
            Kind = kind,
            OccurredAt = now,
            ExpiresAt = expiresAt
        };

    private static async Task<IResult> IssueAsync(IssueRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users,
        IDataProtectionProvider protection, IConfiguration config, TimeProvider clock,
        PartitionedRateLimiter<Guid> accountLimiter)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (!request.VerifiedRecipient || request.Email is not { Length: > 0 and <= 256 } ||
            !new EmailAddressAttribute().IsValid(request.Email.Trim()))
            return Results.Problem(statusCode: 400, title: "Geçerli e-posta ve doğrulanmış alıcı onayı gerekli.");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId))
            return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var ownerLease = await accountLimiter.AcquireAsync(ownerId, cancellationToken: timeout.Token);
        if (!ownerLease.IsAcquired) return Results.StatusCode(429);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        // Revalidate the Owner after the lock: a waiting revoked session cannot issue a code.
        db.ChangeTracker.Clear();
        var owner = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (owner is null || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
            !context.User.HasClaim("amr", "mfa") || await users.IsLockedOutAsync(owner) || owner.SecurityStamp !=
            context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value)
            return Results.Unauthorized();
        var normalized = users.NormalizeEmail(request.Email.Trim());
        // Reject Owner targets before taking a second account lock (including dual roles).
        var candidate = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.NormalizedEmail == normalized, timeout.Token);
        if (candidate is null || !await users.IsInRoleAsync(candidate, "Staff") || await users.IsInRoleAsync(candidate, "Owner"))
            return Results.Problem(statusCode: 400, title: "Sıfırlama yalnız mevcut Staff hesabı için yapılabilir.");
        var staff = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"NormalizedEmail\" = {normalized} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (staff is null || !staff.EmailConfirmed || !await users.IsInRoleAsync(staff, "Staff") ||
            await users.IsInRoleAsync(staff, "Owner"))
            return Results.Problem(statusCode: 400, title: "Sıfırlama yalnız mevcut Staff hesabı için yapılabilir.");
        using var staffLease = await accountLimiter.AcquireAsync(staff.Id, cancellationToken: timeout.Token);
        if (!staffLease.IsAcquired) return Results.StatusCode(429);
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(30);
        var grantId = Guid.NewGuid();
        var token = await users.GeneratePasswordResetTokenAsync(staff);
        var envelope = protection.CreateProtector(EnvelopePurpose).Protect($"{grantId:N}\n{token}");
        var instance = config["Auth:InstanceId"] ?? throw new InvalidOperationException("Firma kimliği gerekli.");
        db.StaffPasswordResetAudits.Add(Audit(grantId, staff.Id, ownerId, instance, "Issued", now, expiresAt));
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new IssuedResponse(envelope, expiresAt));
    }

    private static async Task<IResult> CompleteAsync(PasswordResetEndpoints.ResetRequest request,
        HttpContext context, IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, IDataProtectionProvider protection, IConfiguration config,
        TimeProvider clock, PartitionedRateLimiter<Guid> accountLimiter)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        // Do not sign out an unrelated account when a reset code is submitted.
        if (context.User.Identity?.IsAuthenticated == true)
            return Results.Problem(statusCode: 409, title: "Parola sıfırlamak için mevcut hesaptan çıkış yapın.");
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 1024 ||
            request.NewPassword != request.ConfirmPassword)
            return Results.Problem(statusCode: 400, title: "Yeni parola ve aynı tekrarı gerekli.");
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 8192) return InvalidToken();
        string[] contents;
        try { contents = protection.CreateProtector(EnvelopePurpose).Unprotect(request.Token).Split('\n', 2); }
        catch (Exception exception) when (exception is CryptographicException or FormatException) { return InvalidToken(); }
        if (contents.Length != 2 || !Guid.TryParseExact(contents[0], "N", out var grantId)) return InvalidToken();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var issued = await db.StaffPasswordResetAudits.AsNoTracking().SingleOrDefaultAsync(entry =>
            entry.GrantId == grantId && entry.Kind == "Issued" && entry.InstanceId == config["Auth:InstanceId"], timeout.Token);
        if (issued is null) return InvalidToken();
        using var lease = await accountLimiter.AcquireAsync(issued.StaffId, cancellationToken: timeout.Token);
        if (!lease.IsAcquired) return Results.StatusCode(429);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        db.ChangeTracker.Clear();
        var staff = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {issued.StaffId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (staff is null || !staff.EmailConfirmed || !await users.IsInRoleAsync(staff, "Staff") ||
            await users.IsInRoleAsync(staff, "Owner") || clock.GetUtcNow() >= issued.ExpiresAt ||
            await db.StaffPasswordResetAudits.AnyAsync(entry => entry.GrantId == grantId && entry.Kind == "Completed", timeout.Token))
            return InvalidToken();
        // Identity validates the token and rotates the hash/stamp; lockout and MFA stay intact.
        var result = await users.ResetPasswordAsync(staff, contents[1], request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "InvalidToken")) return InvalidToken();
            if (result.Errors.All(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
                return Results.Problem(statusCode: 400, title: "Yeni parola parola kurallarını karşılamıyor.");
            throw new InvalidOperationException("Staff parolası sıfırlanamadı.");
        }
        db.StaffPasswordResetAudits.Add(Audit(grantId, staff.Id, staff.Id, issued.InstanceId,
            "Completed", clock.GetUtcNow(), issued.ExpiresAt));
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        await signIn.SignOutAsync();
        return Results.NoContent();
    }
}
