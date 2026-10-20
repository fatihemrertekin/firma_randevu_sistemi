using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public sealed class BookingSmsWorker(IServiceScopeFactory scopes, BookingVerificationSecurity security,
    IBookingSmsDelivery delivery, IDataProtectionProvider protection, TimeProvider clock, ILogger<BookingSmsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!security.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is NpgsqlException or DbUpdateException or OperationCanceledException)
            {
                logger.LogWarning("Telefon doğrulama işi işlenemedi; sonraki çevrimde kontrol edilecek.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        if (!security.Enabled) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await MaintainAsync(db, clock.GetUtcNow(), token);
        if (!security.DispatchEnabled) return;
        BookingSmsJob? job;
        var lease = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await security.LockAsync(db, token);
            var now = clock.GetUtcNow();
            job = await db.BookingSmsJobs.FromSqlInterpolated($"""
                SELECT * FROM "BookingSmsJobs" WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
                AND "ExpiresAt" > {now} AND "Attempts" < 3
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(token);
            if (job is null) return;
            job.Status = "Processing"; job.LeaseId = lease; job.LeaseUntil = now.AddSeconds(30); job.UpdatedAt = now;
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        BookingVerificationSecurity.Payload? payload = null;
        BookingSmsAttempt? attempt = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await security.LockAsync(db, token);
            await db.Entry(job).ReloadAsync(token);
            var item = await db.BookingVerifications.SingleAsync(x => x.Id == job.ChallengeId, token);
            var now = clock.GetUtcNow();
            if (job.LeaseId != lease || job.Status != "Processing") return;
            if (job.LeaseUntil <= now) Finish(job, "Unknown", now);
            else if (item.CodeVersion != job.CodeVersion || item.VerifiedAt is not null || item.FailedAttempts >= 5 || item.ExpiresAt <= now)
                Finish(job, "Cancelled", now);
            else
            {
                try
                {
                    payload = JsonSerializer.Deserialize<BookingVerificationSecurity.Payload>(
                        protection.CreateProtector(BookingVerificationSecurity.PayloadPurpose).Unprotect(job.ProtectedPayload ?? ""));
                }
                catch (Exception error) when (error is CryptographicException or JsonException) { Finish(job, "Failed", now); }
                if (payload is null) Finish(job, "Failed", now);
                else
                {
                    // Eski/restore edilmiş, rezervasyonsuz kuyruğu yeni bütçeye sessizce taşımayız.
                    var hasReservation = await db.BookingSmsAttempts.AnyAsync(x => x.JobId == job.Id && x.Number == 1, token);
                    var reservation = hasReservation ? await BookingSmsBudget.ReserveAsync(db, security, job.Id, job.Attempts + 1, now, token) : new BookingSmsBudget.Reservation(null);
                    if (reservation.Attempt is null)
                    {
                        if (reservation.WaitUntil is { } until) { job.NextAttemptAt = until; Release(job, "Pending", now); }
                        else Finish(job, "Cancelled", now);
                        payload = null;
                    }
                    else
                    {
                        var wait = await BookingVerificationQuota.ReserveCallAsync(db, now, token);
                        if (wait is not null) { job.NextAttemptAt = wait.Value; Release(job, "Pending", now); payload = null; }
                        else
                        {
                            attempt = reservation.Attempt; attempt.StartedAt = now; attempt.LeaseId = lease; attempt.Outcome = "Started";
                            job.Attempts++; job.UpdatedAt = now;
                            if (reservation.Warning) logger.LogWarning("OTP gönderim bütçesi yüzde 80 uyarı sınırına ulaştı; operatör kontrolü gerekli.");
                        }
                    }
                }
            }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        if (payload is null || job.Status != "Processing" || attempt is null) return;
        var result = new BookingSmsResult(BookingSmsOutcome.Unknown);
        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            result = await delivery.SendAsync(payload.Phone, payload.Code, job.Id, sendTimeout.Token);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or TimeoutException)
        {
            // Teslim belirsizken tekrar SMS göndermek yerine lease/Unknown korunur.
        }
        using var acknowledgement = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acknowledgement.CancelAfter(TimeSpan.FromSeconds(3));
        await using (var transaction = await db.Database.BeginTransactionAsync(acknowledgement.Token))
        {
            await security.LockAsync(db, acknowledgement.Token);
            await db.Entry(job).ReloadAsync(acknowledgement.Token);
            await db.Entry(attempt).ReloadAsync(acknowledgement.Token);
            var now = clock.GetUtcNow();
            attempt.ProviderReference = result.ProviderReference;
            attempt.Outcome = job.LeaseId == lease && job.Status == "Processing" && job.LeaseUntil > now
                ? result.Outcome.ToString() : "Unknown";
            if (job.LeaseId != lease || job.Status != "Processing")
            {
                await db.SaveChangesAsync(acknowledgement.Token); await transaction.CommitAsync(acknowledgement.Token); return;
            }
            if (job.LeaseUntil <= now) Finish(job, "Unknown", now);
            else if (result.Outcome == BookingSmsOutcome.TemporaryFailure && job.Attempts < 3 && job.ExpiresAt > now.AddSeconds(job.Attempts == 1 ? 15 : 30))
            {
                job.NextAttemptAt = now.AddSeconds(job.Attempts == 1 ? 15 : 30); Release(job, "Pending", now);
            }
            else Finish(job, result.Outcome == BookingSmsOutcome.Accepted ? "Accepted" : result.Outcome == BookingSmsOutcome.Unknown ? "Unknown" : "Failed", now);
            await db.SaveChangesAsync(acknowledgement.Token); await transaction.CommitAsync(acknowledgement.Token);
        }
    }

    private static void Release(BookingSmsJob job, string status, DateTimeOffset now)
    {
        job.Status = status; job.LeaseId = null; job.LeaseUntil = null; job.UpdatedAt = now;
    }
    private static void Finish(BookingSmsJob job, string status, DateTimeOffset now)
    {
        Release(job, status, now); job.ProtectedPayload = null;
    }

    private async Task MaintainAsync(AppDbContext db, DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await security.LockAsync(db, token);
        var expired = await db.BookingVerifications.Where(x => x.ExpiresAt <= now && (x.CodeHash != null || x.ProtectedPhone != null))
            .OrderBy(x => x.ExpiresAt).Take(100).ToArrayAsync(token);
        foreach (var item in expired) { item.CodeHash = null; item.ProtectedPhone = null; item.UsedCodeHashes = []; }
        var stale = await db.BookingSmsJobs.Where(x => x.Status == "Processing" && x.LeaseUntil <= now ||
                x.ExpiresAt <= now && x.ProtectedPayload != null)
            .OrderBy(x => x.UpdatedAt).Take(100).ToArrayAsync(token);
        foreach (var job in stale) Finish(job, job.Status == "Processing" ? "Unknown" : "Expired", now);
        await db.SaveChangesAsync(token);
        await BookingSmsBudget.MaintainAsync(db, now, token);
        var before = now.AddHours(-24);
        var ids = await db.BookingVerifications.Where(x => x.UpdatedAt < before && x.ExpiresAt < before &&
                (x.ReplayUntil == null || x.ReplayUntil <= now) &&
                !db.BookingSmsJobs.Any(j => j.ChallengeId == x.Id && j.Status == "Processing"))
            .OrderBy(x => x.UpdatedAt).Select(x => x.Id).Take(100).ToArrayAsync(token);
        await db.BookingSmsJobs.Where(x => ids.Contains(x.ChallengeId)).ExecuteDeleteAsync(token);
        await db.BookingVerifications.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(token);
        var events = await db.BookingVerificationQuotaEvents.Where(x => x.RetainUntil <= now).OrderBy(x => x.RetainUntil)
            .Select(x => x.Id).Take(100).ToArrayAsync(token);
        await db.BookingVerificationQuotaEvents.Where(x => events.Contains(x.Id)).ExecuteDeleteAsync(token);
        var periods = await db.BookingSmsPeriods.Where(x => x.EndsAt < before).OrderBy(x => x.EndsAt).Select(x => x.Key).Take(100).ToArrayAsync(token);
        await db.BookingSmsPeriods.Where(x => periods.Contains(x.Key)).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token);
        db.ChangeTracker.Clear();
    }
}
