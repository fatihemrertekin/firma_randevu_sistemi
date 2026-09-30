using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class PasswordEndpoints
{
    public static void MapPasswordEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/change-password", ChangeAsync)
            .RequireAuthorization("Owner").RequireRateLimiting("login");
    }

    private static async Task<IResult> ChangeAsync(
        ChangePasswordRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn,
        ILoggerFactory loggerFactory)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Geçersiz istek doğrulaması.");
        }
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword) ||
            request.CurrentPassword.Length > 1024 || request.NewPassword.Length > 1024 ||
            request.NewPassword != request.ConfirmPassword)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Parola alanları gerekli ve yeni parola tekrarı aynı olmalı.");
        }
        if (!Guid.TryParse(users.GetUserId(context.User), out var userId))
        {
            return Results.Unauthorized();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        // Authentication has already tracked this user. Read fresh state under the same row
        // lock as MFA recovery, so a waiting request cannot overwrite a committed password.
        db.ChangeTracker.Clear();
        var user = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (user is null || !user.TwoFactorEnabled || !await users.IsInRoleAsync(user, "Owner"))
        {
            return Results.Forbid();
        }
        if (user.SecurityStamp != context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Oturum değişti. Yeniden giriş yapın.");
        }
        if (await users.IsLockedOutAsync(user))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Hesap geçici olarak kilitli. Daha sonra yeniden giriş yapın.");
        }

        // Identity validates the current password, applies its password validators and
        // saves the new hash and security stamp together. Do not refresh this session.
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == "PasswordMismatch"))
            {
                if (!(await users.AccessFailedAsync(user)).Succeeded)
                {
                    throw new InvalidOperationException("Parola denemesi kaydedilemedi.");
                }
                await transaction.CommitAsync(timeout.Token);
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Mevcut parola doğrulanamadı.");
            }
            if (result.Errors.All(error => error.Code.StartsWith("Password", StringComparison.Ordinal)))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Yeni parola parola kurallarını karşılamıyor.");
            }
            if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Hesap değişti. Yeniden giriş yapın.");
            }
            throw new InvalidOperationException("Parola değiştirilemedi.");
        }
        if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded)
        {
            throw new InvalidOperationException("Parola denemeleri sıfırlanamadı.");
        }
        await transaction.CommitAsync(timeout.Token);
        await signIn.SignOutAsync();
        loggerFactory.CreateLogger("OwnerPasswordChange").LogInformation(
            "Owner password changed. UserId: {UserId}, CorrelationId: {CorrelationId}", userId, context.TraceIdentifier);
        return Results.NoContent();
    }

    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
}
