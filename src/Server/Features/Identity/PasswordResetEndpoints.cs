using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class PasswordResetEndpoints
{
    public static void MapPasswordResetEndpoints(this RouteGroupBuilder auth) =>
        auth.MapPost("/reset-password", ResetAsync).AllowAnonymous().RequireRateLimiting("login");

    public sealed record ResetRequest(string Token, string NewPassword, string ConfirmPassword);

    private static IResult InvalidToken() => Results.Problem(statusCode: StatusCodes.Status400BadRequest,
        title: "Sıfırlama kodu geçersiz veya süresi dolmuş.");

    private static async Task<IResult> ResetAsync(
        ResetRequest request, HttpContext context, IAntiforgery antiforgery, AppDbContext db,
        UserManager<AppUser> users, SignInManager<AppUser> signIn,
        IDataProtectionProvider protection, IConfiguration configuration, TimeProvider clock,
        PartitionedRateLimiter<Guid> accountLimiter)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz istek doğrulaması.");
        if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 1024 ||
            request.NewPassword != request.ConfirmPassword)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Yeni parola ve aynı tekrarı gerekli.");
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 8192) return InvalidToken();
        string[] contents;
        try
        {
            contents = protection.CreateProtector(IssueOwnerPasswordReset.EnvelopePurpose)
                .Unprotect(request.Token).Split('\n', 2);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return InvalidToken();
        }
        if (contents.Length != 2 || !Guid.TryParseExact(contents[0], "N", out var grantId)) return InvalidToken();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var issued = await db.OwnerPasswordResetAudits.AsNoTracking().SingleOrDefaultAsync(entry =>
            entry.GrantId == grantId && (entry.Kind == "Issued" || entry.Kind == OwnerSelfServiceResetFlow.IssuedKind) &&
                entry.InstanceId == configuration["Auth:InstanceId"], timeout.Token);
        if (issued is null) return InvalidToken();
        using var lease = await accountLimiter.AcquireAsync(issued.OwnerId, cancellationToken: timeout.Token);
        if (!lease.IsAcquired) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        // Share the same account lock with password change, issuance and MFA recovery.
        db.ChangeTracker.Clear();
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {issued.OwnerId} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (user is null || !user.EmailConfirmed || !await users.IsInRoleAsync(user, "Owner") ||
            clock.GetUtcNow() >= issued.ExpiresAt ||
            await db.OwnerPasswordResetAudits.AnyAsync(entry => entry.GrantId == grantId && entry.Kind == "Completed", timeout.Token))
            return InvalidToken();
        OwnerSelfServiceReset? selfService = null;
        if (issued.Kind == OwnerSelfServiceResetFlow.IssuedKind)
        {
            selfService = await db.OwnerSelfServiceResets.SingleOrDefaultAsync(entry => entry.OwnerId == user.Id, timeout.Token);
            var email = await db.OwnerRecoveryEmails.SingleOrDefaultAsync(entry => entry.OwnerId == user.Id, timeout.Token);
            if (selfService is null || selfService.GrantId != grantId ||
                selfService.Status is not ("Delivered" or "Processing" or "Pending") ||
                !OwnerSelfServiceResetFlow.Matches(selfService, user, email)) return InvalidToken();
        }
        // ResetPasswordAsync checks the protected Identity token and changes hash/stamp;
        // it does not clear MFA, lockout or recovery codes. Do not refresh any session.
        var result = await users.ResetPasswordAsync(user, contents[1], request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "InvalidToken")) return InvalidToken();
            if (result.Errors.All(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Yeni parola parola kurallarını karşılamıyor.");
            throw new InvalidOperationException("Parola sıfırlanamadı.");
        }
        db.OwnerPasswordResetAudits.Add(new OwnerPasswordResetAudit
        {
            Id = Guid.NewGuid(),
            GrantId = grantId,
            OwnerId = user.Id,
            InstanceId = issued.InstanceId,
            OperatorReference = issued.OperatorReference,
            RequestReference = issued.RequestReference,
            Kind = "Completed",
            OccurredAt = clock.GetUtcNow(),
            ExpiresAt = issued.ExpiresAt
        });
        if (selfService is not null)
        {
            selfService.Status = "Completed";
            selfService.ProtectedPayload = null;
            selfService.LeaseId = null;
            selfService.LeaseUntil = null;
        }
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        await signIn.SignOutAsync();
        return Results.NoContent();
    }
}
