using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public sealed class AppointmentReminderWorker(IServiceScopeFactory scopes, AppointmentReminders reminders,
    BookingVerificationSecurity security, IAppointmentReminderDelivery delivery, IDataProtectionProvider protection,
    TimeProvider clock, ILogger<AppointmentReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!reminders.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is NpgsqlException or DbUpdateException or OperationCanceledException)
            { logger.LogWarning("Randevu hatırlatma işi tamamlanamadı; sonraki çevrimde kontrol edilecek."); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        if (!reminders.Enabled) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lease = Guid.NewGuid();
        AppointmentReminderJob? job;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await security.LockAsync(db, token);
            var now = clock.GetUtcNow();
            var stale = await db.AppointmentReminderJobs.FromSqlInterpolated($"""
                SELECT * FROM "AppointmentReminderJobs" WHERE
                ("Status" = 'Processing' AND "LeaseUntil" <= {now}) OR
                ("Status" IN ('Pending','Processing') AND "Deadline" <= {now})
                ORDER BY "Deadline", "Id" LIMIT 100 FOR UPDATE SKIP LOCKED
                """).ToArrayAsync(token);
            foreach (var old in stale) AppointmentReminders.Finish(old, old.Status == "Processing" ? "Unknown" : "Expired");
            await db.SaveChangesAsync(token);
            await BookingSmsBudget.MaintainAsync(db, now, token);
            if (!security.DispatchEnabled) { await transaction.CommitAsync(token); return; }
            job = await db.AppointmentReminderJobs.FromSqlInterpolated($"""
                SELECT * FROM "AppointmentReminderJobs" WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
                AND "DueAt" <= {now} AND "Deadline" > {now} AND "Attempts" < 3
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(token);
            if (job is null) { await transaction.CommitAsync(token); return; }
            job.Status = "Processing"; job.LeaseId = lease; job.LeaseUntil = now.AddSeconds(30);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        string? phone = null;
        BookingSmsAttempt? attempt = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            var appointment = await LockAppointmentAsync(db, job.AppointmentId, token);
            await security.LockAsync(db, token);
            await db.Entry(job).ReloadAsync(token);
            var block = await db.AppointmentTimeBlocks.SingleAsync(x => x.AppointmentId == job.AppointmentId, token);
            var now = clock.GetUtcNow();
            if (job.Status != "Processing" || job.LeaseId != lease) return;
            if (job.LeaseUntil <= now) AppointmentReminders.Finish(job, "Unknown");
            else if (job.SuppressedAt is not null || block.Status != "Confirmed" || appointment.Version != job.AppointmentVersion ||
                block.StartsAt != job.StartsAt.UtcDateTime || now < job.DueAt || now >= job.Deadline ||
                now >= job.StartsAt || job.BudgetApprovalId != security.BudgetApprovalId)
                AppointmentReminders.Finish(job, "Cancelled");
            else
            {
                try { phone = protection.CreateProtector(AppointmentReminders.PhonePurpose).Unprotect(job.ProtectedPhone ?? ""); }
                catch (CryptographicException) { AppointmentReminders.Finish(job, "Failed"); }
                if (phone is not null && (!TurkishPhone.TryNormalize(phone, out var canonical, out _) || canonical != phone || !phone.StartsWith("+905", StringComparison.Ordinal)))
                { phone = null; AppointmentReminders.Finish(job, "Failed"); }
                if (phone is not null)
                {
                    var reservation = await BookingSmsBudget.ReserveAsync(db, security, job.Id, job.Attempts + 1, now, token,
                        "Reminder", delivery.MaximumUnitPriceTry);
                    if (reservation.Attempt is null)
                    {
                        phone = null;
                        if (reservation.WaitUntil is { } until && until < job.Deadline)
                        { job.NextAttemptAt = until; job.Status = "Pending"; job.LeaseId = null; job.LeaseUntil = null; }
                        else AppointmentReminders.Finish(job, "Budget");
                    }
                    else
                    {
                        attempt = reservation.Attempt; attempt.StartedAt = now; attempt.LeaseId = lease; attempt.Outcome = "Started";
                        job.StartedOnceAt ??= now; job.Attempts++;
                        if (reservation.Warning) logger.LogWarning("SMS gönderim bütçesi yüzde 80 uyarı sınırına ulaştı; operatör kontrolü gerekli.");
                    }
                }
            }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        }
        if (phone is null || attempt is null || job.Status != "Processing") return;
        var result = new BookingSmsResult(BookingSmsOutcome.Unknown);
        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            result = await delivery.SendAsync(phone, job.StartsAt, job.Id, sendTimeout.Token);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or TimeoutException)
        { /* Sağlayıcı kabul etmiş olabilir; kör tekrar yapılmaz. */ }
        using var acknowledgement = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acknowledgement.CancelAfter(TimeSpan.FromSeconds(5)); var ack = acknowledgement.Token;
        await using (var transaction = await db.Database.BeginTransactionAsync(ack))
        {
            var appointment = await LockAppointmentAsync(db, job.AppointmentId, ack);
            await security.LockAsync(db, ack);
            await db.Entry(job).ReloadAsync(ack); await db.Entry(attempt).ReloadAsync(ack);
            var now = clock.GetUtcNow();
            var current = job.Status == "Processing" && job.LeaseId == lease && job.LeaseUntil > now &&
                job.AppointmentVersion == appointment.Version && now < job.Deadline && job.SuppressedAt is null;
            attempt.ProviderReference = result.ProviderReference; attempt.Outcome = current ? result.Outcome.ToString() : "Unknown";
            if (job.Status == "Processing" && job.LeaseId == lease)
            {
                var next = now.AddSeconds(job.Attempts == 1 ? 15 : 30);
                if (current && result.Outcome == BookingSmsOutcome.TemporaryFailure && job.Attempts < 3 && next < job.Deadline)
                { job.Status = "Pending"; job.NextAttemptAt = next; job.LeaseId = null; job.LeaseUntil = null; }
                else AppointmentReminders.Finish(job, !current || result.Outcome == BookingSmsOutcome.Unknown ? "Unknown" :
                    result.Outcome == BookingSmsOutcome.Accepted ? "Accepted" : "Failed");
            }
            await db.SaveChangesAsync(ack); await transaction.CommitAsync(ack);
        }
    }

    private static Task<Appointment> LockAppointmentAsync(AppDbContext db, Guid id, CancellationToken token) =>
        db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").AsNoTracking().SingleAsync(token);
}
