using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });

        auth.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            if (!await HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "E-posta ve parola gerekli.");
            }

            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "E-posta veya parola hatalı.");
            }

            var result = await signIn.PasswordSignInAsync(user, request.Password,
                isPersistent: false, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "E-posta veya parola hatalı.");
            }

            return Results.NoContent();
        }).RequireRateLimiting("login");

        auth.MapPost("/logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            SignInManager<AppUser> signIn) =>
        {
            if (!await HasValidCsrfAsync(antiforgery, context))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Geçersiz istek doğrulaması.");
            }

            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (HttpContext context, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new { email = user.Email });
        }).RequireAuthorization("Owner");
    }

    private static async Task<bool> HasValidCsrfAsync(IAntiforgery antiforgery, HttpContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    public sealed record LoginRequest(string Email, string Password);
}
