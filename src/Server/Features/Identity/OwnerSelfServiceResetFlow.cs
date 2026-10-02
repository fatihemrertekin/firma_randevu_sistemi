using System.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public interface IOwnerPasswordResetDelivery
{
    bool CanDeliver(string email);
    bool Available => CanDeliver("probe@example.test");
    Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, Guid deliveryId, CancellationToken cancellationToken);
}

public sealed class OwnerSelfServiceResetFlow(IConfiguration configuration, IWebHostEnvironment environment,
    IDataProtectionProvider protection, TimeProvider clock)
{
    public const string OutboxPurpose = "OwnerSelfServiceReset.Outbox.v1";
    public const string OperatorReference = "self-service";
    public const string IssuedKind = "SelfIssued";
    public bool Enabled { get; } = ValidateEnabled(configuration, environment);
    private static bool ValidateEnabled(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var local = configuration.GetValue<bool>("OwnerPasswordReset:LocalEnabled");
        var smtp = configuration.GetValue<bool>("OwnerPasswordReset:Enabled");
        if (local && !environment.IsDevelopment())
            throw new InvalidOperationException("Yerel otomatik sıfırlama yalnız geliştirme ortamında açılabilir.");
        if (smtp && (local || configuration["IdentityEmail:Mode"] != "Smtp" ||
            string.IsNullOrWhiteSpace(configuration["Auth:InstanceId"])))
            throw new InvalidOperationException("Otomatik sıfırlama için açık SMTP modu ve firma kimliği gerekli.");
        return local || smtp;
    }

    public static bool Matches(OwnerSelfServiceReset job, AppUser owner, OwnerRecoveryEmail? email) =>
        owner.EmailConfirmed && owner.TwoFactorEnabled && email?.VerifiedAt is not null &&
        email.EmailHash == job.EmailHash && job.EmailHash == OwnerRecoveryEmailEndpoints.Hash(owner.NormalizedEmail ?? "") &&
        job.StampHash == OwnerRecoveryEmailEndpoints.Hash(owner.SecurityStamp ?? "");

    public async Task RequestAsync(string input, AppDbContext db, UserManager<AppUser> users,
        IOwnerPasswordResetDelivery delivery, CancellationToken cancellationToken)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(input) || input.Length > 254) return;
        var normalized = users.NormalizeEmail(input.Trim());
        var id = await db.Users.Where(owner => owner.NormalizedEmail == normalized)
            .Select(owner => (Guid?)owner.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var owner = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var email = await db.OwnerRecoveryEmails.SingleOrDefaultAsync(entry => entry.OwnerId == id, cancellationToken);
        if (!owner.EmailConfirmed || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
            email?.VerifiedAt is null || email.EmailHash != OwnerRecoveryEmailEndpoints.Hash(owner.NormalizedEmail ?? "") ||
            owner.NormalizedEmail != normalized || !delivery.CanDeliver(owner.Email ?? "")) return;
        var job = await db.OwnerSelfServiceResets.SingleOrDefaultAsync(entry => entry.OwnerId == id, cancellationToken);
        var now = clock.GetUtcNow();
        if (job is not null && now < job.RequestedAt.AddMinutes(1)) return;
        if (job is null)
        {
            job = new OwnerSelfServiceReset { OwnerId = owner.Id, EmailHash = "", StampHash = "", Status = "Pending" };
            db.OwnerSelfServiceResets.Add(job);
        }
        var grantId = Guid.NewGuid();
        var token = await users.GeneratePasswordResetTokenAsync(owner);
        var envelope = protection.CreateProtector(IssueOwnerPasswordReset.EnvelopePurpose).Protect($"{grantId:N}\n{token}");
        job.GrantId = grantId;
        job.EmailHash = email.EmailHash;
        job.StampHash = OwnerRecoveryEmailEndpoints.Hash(owner.SecurityStamp ?? "");
        job.ProtectedPayload = protection.CreateProtector(OutboxPurpose).Protect(envelope);
        job.RequestedAt = now;
        job.ExpiresAt = now.AddMinutes(30);
        job.Status = "Pending";
        job.Attempts = 0;
        job.NextAttemptAt = now;
        job.LeaseId = null;
        job.LeaseUntil = null;
        db.OwnerPasswordResetAudits.Add(new OwnerPasswordResetAudit
        {
            Id = Guid.NewGuid(),
            GrantId = grantId,
            OwnerId = owner.Id,
            InstanceId = configuration["Auth:InstanceId"] ?? "firma-randevu-development",
            OperatorReference = OperatorReference,
            RequestReference = grantId.ToString("N"),
            Kind = IssuedKind,
            OccurredAt = now,
            ExpiresAt = job.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public sealed record RequestBody(string Email);
    public static void MapEndpoint(RouteGroupBuilder auth)
    {
        auth.MapGet("/password-reset-options", (HttpContext context, OwnerSelfServiceResetFlow flow,
            IOwnerPasswordResetDelivery delivery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { available = flow.Enabled && delivery.Available });
        }).AllowAnonymous();
        auth.MapPost("/password-reset-request", async (RequestBody request, HttpContext context,
            IAntiforgery antiforgery, OwnerSelfServiceResetFlow flow, AppDbContext db, UserManager<AppUser> users,
            IOwnerPasswordResetDelivery delivery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
                return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
            var elapsed = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await flow.RequestAsync(request.Email, db, users, delivery, timeout.Token);
            var remaining = TimeSpan.FromMilliseconds(350) - elapsed.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, timeout.Token);
            return Results.Accepted(value: new { message = "Bilgiler uygunsa e-postanıza sıfırlama bağlantısı hazırlanacaktır." });
        }).AllowAnonymous().RequireRateLimiting("login");
    }
}
