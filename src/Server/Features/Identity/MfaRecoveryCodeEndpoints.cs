using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class MfaRecoveryCodeEndpoints
{
    public static void MapMfaRecoveryCodeEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapGet("/mfa/recovery-codes", async (HttpContext context, UserManager<AppUser> users) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(new
            {
                remaining = await users.CountRecoveryCodesAsync(user)
            });
        }).RequireAuthorization("Owner");
        auth.MapPost("/mfa/recovery-codes", ReplaceAsync)
            .RequireAuthorization("Owner").RequireRateLimiting("login");
    }

    public sealed record ReplaceRequest(string CurrentPassword);

    private static async Task<IResult> ReplaceAsync(
        ReplaceRequest request, HttpContext context, IAntiforgery antiforgery, AppDbContext db,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, ILoggerFactory loggerFactory)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 1024)
            return Results.Problem(statusCode: 400, title: "Mevcut parola gerekli.");
        if (!Guid.TryParse(users.GetUserId(context.User), out var userId)) return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        // Serialize with password changes and operator recovery; recheck the authenticated stamp.
        db.ChangeTracker.Clear();
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (user is null || !user.TwoFactorEnabled || !await users.IsInRoleAsync(user, "Owner") ||
            !context.User.HasClaim("amr", "mfa")) return Results.Forbid();
        if (user.SecurityStamp != context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value)
            return Results.Problem(statusCode: 409, title: "Oturum değişti. Yeniden giriş yapın.");
        if (await users.IsLockedOutAsync(user))
            return Results.Problem(statusCode: 400, title: "Hesap geçici olarak kilitli. Daha sonra yeniden giriş yapın.");
        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            if (!(await users.AccessFailedAsync(user)).Succeeded) throw new InvalidOperationException("Parola denemesi kaydedilemedi.");
            await transaction.CommitAsync(timeout.Token);
            return Results.Problem(statusCode: 400, title: "Mevcut parola doğrulanamadı.");
        }
        var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 8))?.ToArray();
        if (recoveryCodes is not { Length: 8 } || !(await users.ResetAccessFailedCountAsync(user)).Succeeded ||
            !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Kurtarma kodları yenilenemedi.");
        await transaction.CommitAsync(timeout.Token);
        await signIn.SignOutAsync();
        loggerFactory.CreateLogger("MfaRecoveryCodes").LogInformation(
            "MFA recovery codes replaced. UserId: {UserId}, CorrelationId: {CorrelationId}", userId, context.TraceIdentifier);
        return Results.Ok(new { recoveryCodes });
    }
}
