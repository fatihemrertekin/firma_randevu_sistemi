using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class IdentityStartupExtensions
{
    public static void AddApplicationIdentity(this WebApplicationBuilder builder)
    {
        ConfigureDataProtection(builder);
        var secureCookie = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        AddIdentityServices(builder);
        AddResetDeliveryServices(builder);
        ConfigureCookies(builder, secureCookie);
        ConfigureAccessProtection(builder, secureCookie);
    }

    private static void ConfigureDataProtection(WebApplicationBuilder builder)
    {
        var instanceId = builder.Configuration["Auth:InstanceId"];
        var keysDirectory = builder.Configuration["Auth:KeysDirectory"];
        if (builder.Environment.IsDevelopment())
        {
            instanceId ??= "firma-randevu-development";
            keysDirectory ??= Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
        }
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(keysDirectory))
        {
            throw new InvalidOperationException("Auth:InstanceId ve Auth:KeysDirectory gerekli.");
        }
        Directory.CreateDirectory(keysDirectory);
        builder.Services.AddDataProtection()
            .SetApplicationName(instanceId)
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
    }

    private static void AddIdentityServices(WebApplicationBuilder builder)
    {
        builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.SignIn.RequireConfirmedAccount = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
                options.Tokens.PasswordResetTokenProvider = "OwnerPasswordReset";
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddUserConfirmation<ActiveUserConfirmation>()
            .AddTokenProvider<OwnerPasswordResetTokenProvider>("OwnerPasswordReset");
        builder.Services.AddScoped<ISecurityStampValidator, ActiveSecurityStampValidator>();
    }

    private static void AddResetDeliveryServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IdentityEmailOptions>();
        builder.Services.AddSingleton<IIdentityEmailTransport, SmtpIdentityEmailTransport>();
        builder.Services.AddSingleton<SmtpOwnerEmailDelivery>();
        builder.Services.AddSingleton<LocalOwnerEmailVerificationDelivery>();
        builder.Services.AddSingleton<IOwnerEmailVerificationDelivery>(services =>
            (services.GetRequiredService<IConfiguration>()["IdentityEmail:Mode"] ?? "Local") switch
            {
                "Smtp" => services.GetRequiredService<SmtpOwnerEmailDelivery>(),
                "Local" => services.GetRequiredService<LocalOwnerEmailVerificationDelivery>(),
                _ => throw new InvalidOperationException("Kimlik e-postası teslim modu geçersiz.")
            });
        builder.Services.AddSingleton<IOwnerPasswordResetDelivery>(services =>
            (IOwnerPasswordResetDelivery)services.GetRequiredService<IOwnerEmailVerificationDelivery>());
        builder.Services.AddSingleton<OwnerSelfServiceResetFlow>();
        builder.Services.AddHostedService<OwnerResetDeliveryWorker>();
        builder.Services.AddSingleton(PartitionedRateLimiter.Create<Guid, Guid>(ownerId =>
            RateLimitPartition.GetFixedWindowLimiter(ownerId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5)
            })));
    }

    private static void ConfigureCookies(WebApplicationBuilder builder, CookieSecurePolicy secureCookie)
    {
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = builder.Environment.IsDevelopment()
                ? "FirmaRandevu.Dev"
                : "__Host-FirmaRandevu";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secureCookie;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = builder.Environment.IsDevelopment()
                ? "FirmaRandevu.TwoFactor.Dev"
                : "__Host-FirmaRandevu.TwoFactor";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = secureCookie;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
            options.Events.OnSigningIn = async context =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.Name);
                var user = userId is null ? null : await users.FindByIdAsync(userId);
                if (user?.SecurityStamp is not null && context.Principal?.Identity is ClaimsIdentity identity)
                {
                    identity.AddClaim(new Claim("mfa_security_stamp", user.SecurityStamp));
                }
            };
            options.Events.OnValidatePrincipal = async context =>
            {
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var userId = context.Principal?.FindFirstValue(ClaimTypes.Name);
                var user = userId is null ? null : await users.FindByIdAsync(userId);
                var stamp = context.Principal?.FindFirstValue("mfa_security_stamp");
                if (user is null || !user.IsActive || !user.TwoFactorEnabled || string.IsNullOrEmpty(stamp) || stamp != user.SecurityStamp)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
                }
            };
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
            options.OnRefreshingPrincipal = context =>
            {
                // Identity geçerli stamp sonrası kimliği yeniden kurar. Doğrulanmış
                // cookie'nin MFA bilgisini koru; Owner yetkisi yenilemede kaybolmasın.
                if (context.CurrentPrincipal?.HasClaim("amr", "mfa") == true &&
                    context.NewPrincipal?.Identity is ClaimsIdentity identity)
                {
                    identity.AddClaim(new Claim("amr", "mfa"));
                }
                return Task.CompletedTask;
            };
        });
    }

    private static void ConfigureAccessProtection(WebApplicationBuilder builder, CookieSecurePolicy secureCookie)
    {
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("OwnerSetup", policy => policy.RequireRole("Owner"))
            .AddPolicy("Owner", policy => policy.RequireRole("Owner").RequireClaim("amr", "mfa"))
            .AddPolicy("PasswordChange", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
                context.User.IsInRole("Owner")
                    ? context.User.HasClaim("amr", "mfa")
                    : context.User.IsInRole("Staff")));
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.SecurePolicy = secureCookie;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("all-requests", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 600,
                    Window = TimeSpan.FromMinutes(1)
                }));
            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    (context.Connection.RemoteIpAddress?.ToString() ?? "unknown") + context.Request.Path,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5)
                    }));
            options.AddPolicy("staff-management", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1)
                }));
        });
    }
}
