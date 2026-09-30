using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class MfaEndpoints
{
    public static void MapMfaEndpoints(this RouteGroupBuilder auth)
    {
        var mfa = auth.MapGroup("/mfa");

        mfa.MapPost("/setup", async (
            PasswordRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }

            var user = await users.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (user.TwoFactorEnabled)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "İki aşamalı giriş zaten açık.");
            }
            if (!await HasValidPasswordAsync(users, user, request.Password))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Parola doğrulanamadı.");
            }

            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                    title: "Doğrulayıcı anahtarı oluşturulamadı.");
            }
            await users.ResetAccessFailedCountAsync(user);
            await signIn.RefreshSignInAsync(user);
            var key = await users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Doğrulayıcı anahtarı kaydedilemedi.");
            }

            const string issuer = "Firma Randevu";
            var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(user.Email ?? "Owner");
            var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { key, uri });
        }).RequireAuthorization("OwnerSetup").RequireRateLimiting("login");

        mfa.MapPost("/enable", async (
            EnableRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            AppDbContext db,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }

            var user = await users.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.Unauthorized();
            }
            if (user.TwoFactorEnabled)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "İki aşamalı giriş zaten açık.");
            }
            if (!await HasValidPasswordAsync(users, user, request.Password))
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Parola doğrulanamadı.");
            }

            var key = await users.GetAuthenticatorKeyAsync(user);
            var code = request.Code?.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(code) ||
                !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
            {
                await users.AccessFailedAsync(user);
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Doğrulama kodu hatalı.");
            }

            await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
            await users.ResetAccessFailedCountAsync(user);
            var enabled = await users.SetTwoFactorEnabledAsync(user, true);
            if (!enabled.Succeeded)
            {
                throw new InvalidOperationException("İki aşamalı giriş etkinleştirilemedi.");
            }
            var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 8))?.ToArray();
            if (recoveryCodes is not { Length: 8 })
            {
                throw new InvalidOperationException("Kurtarma kodları oluşturulamadı.");
            }
            var updated = await users.UpdateSecurityStampAsync(user);
            if (!updated.Succeeded)
            {
                throw new InvalidOperationException("Oturumlar yenilenemedi.");
            }
            await transaction.CommitAsync(context.RequestAborted);

            await signIn.SignOutAsync();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { recoveryCodes });
        }).RequireAuthorization("OwnerSetup").RequireRateLimiting("login");

        mfa.MapPost("/login", async (
            CodeRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<AppUser> signIn) =>
        {
            if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Doğrulama kodu gerekli.");
            }

            var result = await signIn.TwoFactorAuthenticatorSignInAsync(
                request.Code.Replace(" ", string.Empty, StringComparison.Ordinal),
                isPersistent: false, rememberClient: false);
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Doğrulama kodu hatalı.");
        }).RequireRateLimiting("login");

        mfa.MapPost("/recovery-login", async (
            CodeRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<AppUser> signIn) =>
        {
            if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Kurtarma kodu gerekli.");
            }

            var result = await signIn.TwoFactorRecoveryCodeSignInAsync(request.Code.Trim());
            return result.Succeeded
                ? Results.NoContent()
                : Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Kurtarma kodu hatalı.");
        }).RequireRateLimiting("login");
    }

    public sealed record PasswordRequest(string Password);
    public sealed record EnableRequest(string Password, string Code);
    public sealed record CodeRequest(string Code);

    private static async Task<bool> HasValidPasswordAsync(
        UserManager<AppUser> users, AppUser user, string? password)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return false;
        }
        if (!string.IsNullOrEmpty(password) && await users.CheckPasswordAsync(user, password))
        {
            return true;
        }
        await users.AccessFailedAsync(user);
        return false;
    }
}
