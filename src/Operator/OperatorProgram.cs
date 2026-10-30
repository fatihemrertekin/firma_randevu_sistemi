using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace SubscriptionOperator;

public sealed class OperatorProgram
{
    public static async Task Main(string[] args)
    {
        var app = Build(args);
        if (args is ["initialize-operator"])
        {
            try { Environment.ExitCode = await InitializeAsync(app.Services); }
            catch (Exception e) when (e is Npgsql.NpgsqlException or DbUpdateException or OperationCanceledException or InvalidOperationException { InnerException: Npgsql.NpgsqlException })
            {
                Console.Error.WriteLine("Operatör kurulum sonucu doğrulanamadı. Mevcut hesap ve kayıtları kontrol etmeden tekrar kurulum yapmayın.");
                Environment.ExitCode = 1;
            }
            return;
        }
        await app.RunAsync();
    }

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
        var allowedHosts = builder.Configuration["AllowedHosts"];
        if (!builder.Environment.IsDevelopment() && (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Contains('*')))
            throw new InvalidOperationException("Operatör uygulaması için açık Host allowlist gerekli.");
        var proxy = builder.Configuration["Operator:TrustedProxy"];
        if (!string.IsNullOrWhiteSpace(proxy))
        {
            if (!IPAddress.TryParse(proxy, out var address)) throw new InvalidOperationException("Güvenilen proxy IP adresi geçersiz.");
            builder.Services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear(); o.KnownProxies.Clear(); o.KnownProxies.Add(address); o.ForwardLimit = 1;
            });
        }
        builder.AddOperatorIdentity();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SubscriptionTargets>();
        builder.Services.AddSingleton(s => s.GetRequiredService<SubscriptionTargets>().Single ?? throw new InvalidOperationException("Açık firma seçimi gerekli."));
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.OnRejected = (c, _) =>
            {
                if (c.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)) c.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
            foreach (var policy in new[] { "auth", "ledger" }) o.AddPolicy(policy, c => RateLimitPartition.GetFixedWindowLimiter(
                c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = policy == "auth" ? 30 : 60, Window = TimeSpan.FromMinutes(1) }));
        });
        var app = builder.Build();
        var targets = app.Services.GetRequiredService<SubscriptionTargets>();
        if (!string.IsNullOrWhiteSpace(proxy)) app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.Use(async (c, next) =>
        {
            if (!app.Environment.IsDevelopment() && !c.Request.IsHttps && !c.Request.Path.StartsWithSegments("/health")) { c.Response.StatusCode = 400; return; }
            c.Response.Headers.CacheControl = "no-store";
            c.Response.Headers["Referrer-Policy"] = "no-referrer";
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next(c);
        });
        app.Use(async (c, next) =>
        {
            if (c.Request.Path.StartsWithSegments("/api"))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var identity = c.RequestServices.GetRequiredService<OperatorIdentityDb>();
                    if (await DatabaseInstance.ReadAsync(identity, "app.operator_identity", timeout.Token) != app.Configuration["Operator:InstanceId"])
                    { c.Response.StatusCode = 503; return; }
                }
                catch (Exception e) when (e is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException { InnerException: Npgsql.NpgsqlException } || e is OperationCanceledException && !c.RequestAborted.IsCancellationRequested)
                { c.Response.StatusCode = 503; return; }
            }
            await next(c);
        });
        app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
        app.MapOperatorIdentity(); app.MapSubscriptionLedger();
        targets.MapEndpoints(app);
        app.MapGet("/health/live", () => Results.Ok());
        app.MapGet("/health/ready", async (OperatorIdentityDb identity, CancellationToken token) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                if (!await identity.Database.CanConnectAsync(timeout.Token) ||
                    await DatabaseInstance.ReadAsync(identity, "app.operator_identity", timeout.Token) != app.Configuration["Operator:InstanceId"] ||
                    !await identity.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"AspNetUsers\"') IS NOT NULL AS \"Value\"").SingleAsync(timeout.Token)) return Results.StatusCode(503);
                // Çok firmada bir hedefin kesintisi merkez panelin tamamını sağlıksız yapmaz.
                if (targets.Single is { } target) { await using var connection = await target.OpenAsync(timeout.Token); }
                return Results.Ok();
            }
            catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException or OperationCanceledException or TimeoutException) { return Results.StatusCode(503); }
        });
        app.MapFallback("/api/{**path}", () => Results.NotFound());
        app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("operator.html");
        return app;
    }

    public static async Task<int> InitializeAsync(IServiceProvider services)
    {
        var email = Environment.GetEnvironmentVariable("OPERATOR_BOOTSTRAP_EMAIL");
        var password = Environment.GetEnvironmentVariable("OPERATOR_BOOTSTRAP_PASSWORD");
        if (email is null || email.Length > 254 || !new EmailAddressAttribute().IsValid(email) || string.IsNullOrEmpty(password)) return 1;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OperatorIdentityDb>();
        // Ayrı ve boş kimlik DB'si; firma şeması bu komutla oluşturulamaz/değiştirilemez.
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (await DatabaseInstance.ReadAsync(db, "app.operator_identity", timeout.Token) != config["Operator:InstanceId"]) return 1;
        var tables = await db.Database.SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname='public'").ToArrayAsync(timeout.Token);
        string[] identityTables = ["AspNetUsers", "AspNetRoles", "AspNetRoleClaims", "AspNetUserClaims", "AspNetUserLogins", "AspNetUserRoles", "AspNetUserTokens"];
        if (tables.Length != 0 && !tables.Order().SequenceEqual(identityTables.Order())) return 1;
        await db.Database.EnsureCreatedAsync(timeout.Token);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, timeout.Token);
        if (await db.Users.AnyAsync(timeout.Token)) return 1;
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperatorUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var role = await roles.CreateAsync(new IdentityRole<Guid>("Operator"));
        var user = new OperatorUser { UserName = email.Trim(), Email = email.Trim(), EmailConfirmed = true };
        if (!role.Succeeded || !(await users.CreateAsync(user, password)).Succeeded || !(await users.AddToRoleAsync(user, "Operator")).Succeeded) return 1;
        await transaction.CommitAsync(timeout.Token); return 0;
    }
}
