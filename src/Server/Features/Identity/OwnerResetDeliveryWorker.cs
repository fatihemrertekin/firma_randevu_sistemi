using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Identity;

public sealed class OwnerResetDeliveryWorker(IServiceScopeFactory scopes, OwnerSelfServiceResetFlow flow,
    TimeProvider clock, ILogger<OwnerResetDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!flow.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is NpgsqlException or DbUpdateException or OperationCanceledException)
            {
                // Do not log exception text: it may include connection data or delivery secrets.
                logger.LogWarning("Owner sıfırlama kuyruğu işlenemedi; sonraki çevrimde yeniden denenecek.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        if (!flow.Enabled) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        // Expiry removes bearer material even when a worker died holding a lease.
        await db.OwnerSelfServiceResets.Where(entry => entry.ExpiresAt <= now && entry.ProtectedPayload != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.ProtectedPayload, (string?)null)
                .SetProperty(entry => entry.Status, "Expired").SetProperty(entry => entry.LeaseId, (Guid?)null)
                .SetProperty(entry => entry.LeaseUntil, (DateTimeOffset?)null), timeout.Token);
        await db.OwnerSelfServiceResets.Where(entry => entry.Status == "Processing" && entry.Attempts >= 4 && entry.LeaseUntil <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Status, "Failed")
                .SetProperty(entry => entry.ProtectedPayload, (string?)null).SetProperty(entry => entry.LeaseId, (Guid?)null)
                .SetProperty(entry => entry.LeaseUntil, (DateTimeOffset?)null), timeout.Token);
        OwnerSelfServiceReset? job;
        var leaseId = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            job = await db.OwnerSelfServiceResets.FromSqlInterpolated($"""
                SELECT * FROM "OwnerSelfServiceResets"
                WHERE "ProtectedPayload" IS NOT NULL AND "ExpiresAt" > {now} AND "Attempts" < 4
                AND (("Status" = 'Pending' AND "NextAttemptAt" <= {now})
                  OR ("Status" = 'Processing' AND "LeaseUntil" <= {now}))
                ORDER BY "NextAttemptAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(timeout.Token);
            if (job is null) return;
            job.Status = "Processing";
            job.LeaseId = leaseId;
            job.LeaseUntil = now.AddSeconds(30);
            job.Attempts++;
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        // Sending happens outside a DB transaction. Conditional acknowledgement cannot overwrite a newer grant.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == job.OwnerId, timeout.Token);
        var email = await db.OwnerRecoveryEmails.AsNoTracking().SingleOrDefaultAsync(entry => entry.OwnerId == job.OwnerId, timeout.Token);
        var delivery = scope.ServiceProvider.GetRequiredService<IOwnerPasswordResetDelivery>();
        var status = "Cancelled";
        if (owner is not null && OwnerSelfServiceResetFlow.Matches(job, owner, email) &&
            await users.IsInRoleAsync(owner, "Owner") && delivery.CanDeliver(owner.Email ?? "") &&
            await db.OwnerSelfServiceResets.AsNoTracking().AnyAsync(entry => entry.OwnerId == job.OwnerId &&
                entry.GrantId == job.GrantId && entry.LeaseId == leaseId, timeout.Token))
        {
            try
            {
                var token = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector(OwnerSelfServiceResetFlow.OutboxPurpose).Unprotect(job.ProtectedPayload ?? "");
                using var deliveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                deliveryTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                await delivery.DeliverAsync(owner.Email ?? "", token, job.ExpiresAt, job.GrantId, deliveryTimeout.Token);
                status = "Delivered";
            }
            catch (CryptographicException) { status = "Failed"; }
            catch (IdentityEmailDeliveryException failure)
            { status = failure.Retryable && job.Attempts < 4 ? "Pending" : "Failed"; }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = job.Attempts >= 4 ? "Failed" : "Pending";
            }
        }
        // Use a fresh short acknowledgement window if delivery timed out; leave the lease reclaimable if this fails.
        using var acknowledgement = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acknowledgement.CancelAfter(TimeSpan.FromSeconds(3));
        var retryAt = clock.GetUtcNow().AddSeconds(15 * Math.Pow(2, job.Attempts - 1));
        await db.OwnerSelfServiceResets.Where(entry => entry.OwnerId == job.OwnerId &&
            entry.GrantId == job.GrantId && entry.LeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Status, status)
                .SetProperty(entry => entry.ProtectedPayload, status == "Pending" ? job.ProtectedPayload : null)
                .SetProperty(entry => entry.NextAttemptAt, retryAt)
                .SetProperty(entry => entry.LeaseId, (Guid?)null).SetProperty(entry => entry.LeaseUntil, (DateTimeOffset?)null), acknowledgement.Token);
    }
}
