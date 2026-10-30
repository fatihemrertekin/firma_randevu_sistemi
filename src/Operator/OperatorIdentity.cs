using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SubscriptionOperator;

public sealed class OperatorUser : IdentityUser<Guid> { }

public sealed class OperatorIdentityDb(DbContextOptions<OperatorIdentityDb> options)
    : IdentityDbContext<OperatorUser, IdentityRole<Guid>, Guid>(options);

internal static class OperatorIdentity
{
    internal static void AddOperatorIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<OperatorIdentityDb>((services, o) =>
        {
            var connection = new Npgsql.NpgsqlConnectionStringBuilder(Required(services.GetRequiredService<IConfiguration>(), "ConnectionStrings:OperatorIdentity"))
            { Timeout = 5, CommandTimeout = 15, MaxPoolSize = 8, IncludeErrorDetail = false };
            o.UseNpgsql(connection.ConnectionString);
        });
        builder.Services.AddDataProtection();
        builder.Services.AddOptions<DataProtectionOptions>().Configure<IConfiguration>((o, c) =>
            o.ApplicationDiscriminator = "subscription-operator:" + Required(c, "Operator:InstanceId"));
        builder.Services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, ILoggerFactory>((o, c, log) =>
            o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Required(c, "Operator:KeysDirectory")), log));
        builder.Services.AddIdentity<OperatorUser, IdentityRole<Guid>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<OperatorIdentityDb>().AddDefaultTokenProviders();
        var secure = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        builder.Services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = builder.Environment.IsDevelopment() ? "SubscriptionOperator.Dev" : "__Host-SubscriptionOperator";
            o.Cookie.Path = "/";
            o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = secure; o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            // Tek operatör akışında iptal edilen oturum bir sonraki istekte reddedilir.
            o.Events.OnValidatePrincipal = async c =>
            {
                var users = c.HttpContext.RequestServices.GetRequiredService<UserManager<OperatorUser>>();
                var principal = c.Principal;
                if (principal is null) { c.RejectPrincipal(); return; }
                var user = await users.GetUserAsync(principal);
                if (user is null || !await users.IsInRoleAsync(user, "Operator") || await users.IsLockedOutAsync(user) ||
                    user.SecurityStamp != principal.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType) ||
                    user.TwoFactorEnabled != principal.HasClaim("amr", "mfa")) c.RejectPrincipal();
                c.ShouldRenew = false;
            };
        });
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.Name = builder.Environment.IsDevelopment() ? "SubscriptionOperator.Mfa.Dev" : "__Host-SubscriptionOperator.Mfa";
            o.Cookie.Path = "/";
            o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = secure; o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5); o.SlidingExpiration = false;
        });
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = builder.Environment.IsDevelopment() ? "SubscriptionOperator.Csrf.Dev" : "__Host-SubscriptionOperator.Csrf";
            o.Cookie.Path = "/";
            o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = secure; o.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("OperatorSetup", p => p.RequireAuthenticatedUser().RequireRole("Operator"));
            o.AddPolicy("Operator", p => p.RequireAuthenticatedUser().RequireRole("Operator").RequireClaim("amr", "mfa"));
        });
    }

    private static string Required(IConfiguration config, string name) => string.IsNullOrWhiteSpace(config[name])
        ? throw new InvalidOperationException("Operatör kimlik veritabanı, kurulum kimliği ve ayrı anahtar dizini gerekli.")
        : config[name] ?? throw new InvalidOperationException("Operatör yapılandırması eksik.");

    internal static async Task<bool> Csrf(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException) { return false; }
    }
    internal static IResult InvalidCsrf() => Results.Problem(statusCode: 400, title: "İstek doğrulanamadı. Yeniden deneyin.",
        extensions: new Dictionary<string, object?> { ["code"] = "csrf_invalid" });

    internal static void MapOperatorIdentity(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");
        auth.MapGet("/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
        auth.MapPost("/login", async (Login request, HttpContext c, IAntiforgery a, UserManager<OperatorUser> users, SignInManager<OperatorUser> signIn) =>
        {
            if (!await Csrf(c, a)) return InvalidCsrf();
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return Results.Problem(statusCode: 400, title: "E-posta ve parola gerekli.");
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !await users.IsInRoleAsync(user, "Operator")) return Results.Unauthorized();
            var result = await signIn.PasswordSignInAsync(user, request.Password, false, true);
            return result.RequiresTwoFactor ? Results.Json(new { requiresTwoFactor = true }, statusCode: 202)
                : result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        auth.MapGet("/me", async (HttpContext c, UserManager<OperatorUser> users) =>
        {
            var user = await users.GetUserAsync(c.User);
            return user is null ? Results.Unauthorized() : Results.Ok(new { user.Email, mfaEnabled = user.TwoFactorEnabled, operatorAccess = user.TwoFactorEnabled && c.User.HasClaim("amr", "mfa") });
        }).RequireAuthorization("OperatorSetup");
        auth.MapPost("/logout", async (HttpContext c, IAntiforgery a, SignInManager<OperatorUser> signIn) =>
        {
            if (!await Csrf(c, a)) return InvalidCsrf();
            await signIn.SignOutAsync(); return Results.NoContent();
        });
        auth.MapPost("/mfa/login", async (Code request, HttpContext c, IAntiforgery a, SignInManager<OperatorUser> signIn) =>
        {
            if (!await Csrf(c, a)) return InvalidCsrf();
            if (string.IsNullOrWhiteSpace(request.Value) || request.Value.Length > 32) return Results.BadRequest();
            var result = request.Recovery
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(request.Value.Trim())
                : await signIn.TwoFactorAuthenticatorSignInAsync(request.Value.Replace(" ", "", StringComparison.Ordinal), false, false);
            return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
        });
        auth.MapPost("/mfa/setup", async (Setup request, HttpContext c, IAntiforgery a, OperatorIdentityDb db, UserManager<OperatorUser> users, SignInManager<OperatorUser> signIn) =>
            await SetupAsync(request, c, a, db, users, signIn, false)).RequireAuthorization("OperatorSetup");
        auth.MapPost("/mfa/enable", async (Setup request, HttpContext c, IAntiforgery a, OperatorIdentityDb db, UserManager<OperatorUser> users, SignInManager<OperatorUser> signIn) =>
            await SetupAsync(request, c, a, db, users, signIn, true)).RequireAuthorization("OperatorSetup");
    }

    private static async Task<IResult> SetupAsync(Setup request, HttpContext c, IAntiforgery a, OperatorIdentityDb db,
        UserManager<OperatorUser> users, SignInManager<OperatorUser> signIn, bool enable)
    {
        if (!await Csrf(c, a)) return InvalidCsrf();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var id = users.GetUserId(c.User);
        if (!Guid.TryParse(id, out var userId)) return Results.Unauthorized();
        db.ChangeTracker.Clear();
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\"={userId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (user is null || !await users.IsInRoleAsync(user, "Operator") || user.SecurityStamp != c.User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType)) return Results.Unauthorized();
        if (user.TwoFactorEnabled) return Results.Conflict();
        if (await users.IsLockedOutAsync(user)) return Results.Unauthorized();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024 || !await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user); await transaction.CommitAsync(timeout.Token); return Results.Unauthorized();
        }
        if (!enable)
        {
            if (!(await users.ResetAuthenticatorKeyAsync(user)).Succeeded) throw new InvalidOperationException("MFA anahtarı oluşturulamadı.");
            await users.ResetAccessFailedCountAsync(user); await transaction.CommitAsync(timeout.Token);
            await signIn.RefreshSignInAsync(user); return Results.Ok(new { key = await users.GetAuthenticatorKeyAsync(user) });
        }
        if (request.Value is not { Length: 6 } || !request.Value.All(char.IsAsciiDigit) ||
            !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Value))
        {
            await users.AccessFailedAsync(user); await transaction.CommitAsync(timeout.Token); return Results.BadRequest();
        }
        if (!(await users.SetTwoFactorEnabledAsync(user, true)).Succeeded || !(await users.ResetAccessFailedCountAsync(user)).Succeeded ||
            !(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new InvalidOperationException("MFA açılamadı.");
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 8))?.ToArray();
        if (codes is not { Length: 8 }) throw new InvalidOperationException("Kurtarma kodları oluşturulamadı.");
        await transaction.CommitAsync(timeout.Token); await signIn.SignOutAsync(); return Results.Ok(new { recoveryCodes = codes });
    }

    internal sealed record Login(string Email, string Password);
    internal sealed record Code(string Value, bool Recovery = false);
    internal sealed record Setup(string Password, string? Value = null);
}
