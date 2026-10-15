using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public sealed class AppointmentPendingWorker(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration configuration,
    ILogger<AppointmentPendingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("AppDatabase"))) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(5);
            try { await ProcessBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) when (error is NpgsqlException or DbUpdateException or OperationCanceledException or InvalidOperationException { InnerException: NpgsqlException })
            {
                logger.LogWarning("Bekleyen randevular okunamadı; sonraki çevrimde yeniden denenecek.");
                delay = TimeSpan.FromSeconds(30);
            }
            try { await Task.Delay(delay, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        Guid[] ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = clock.GetUtcNow().UtcDateTime;
            ids = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.Status == "Pending" && item.PendingExpiresAt <= now)
                .OrderBy(item => item.PendingExpiresAt).ThenBy(item => item.AppointmentId).Select(item => item.AppointmentId).Take(20).ToArrayAsync(token);
        }
        var changed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { if (await ExpireAsync(id, token)) changed++; }
            catch (Exception error) when (error is NpgsqlException or DbUpdateException or OperationCanceledException or InvalidOperationException { InnerException: NpgsqlException })
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Hata metni bağlantı veya kişi verisi içerebilir; başarısız kayıt DB'de yeniden seçilebilir kalır.
                logger.LogWarning("Bekleyen randevu iptal edilemedi; sonraki çevrimde yeniden denenecek.");
                if (timeout.IsCancellationRequested) break;
            }
        }
        return changed;
    }

    private async Task<bool> ExpireAsync(Guid id, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var memberId = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.AppointmentId == id).Select(item => (Guid?)item.StaffMemberId).SingleOrDefaultAsync(token);
        if (memberId is null) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // HTTP işlemleriyle aynı parent sırası; meşgul kayıt beklenmez, sonraki çevrimde seçilir.
        if (await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={memberId.Value} FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(token) is null) return false;
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(token);
        if (appointment is null) return false;
        var block = await db.AppointmentTimeBlocks.SingleAsync(item => item.AppointmentId == id, token); var now = clock.GetUtcNow().UtcDateTime;
        if (block.StaffMemberId != memberId || block.Status != "Pending" || block.PendingExpiresAt is not { } deadline || deadline > now) return false;
        block.Status = "Cancelled"; appointment.Version = Guid.NewGuid();
        db.AppointmentAudits.Add(new() { Id = Guid.NewGuid(), AppointmentId = id, ActorId = null, AppointmentVersion = appointment.Version, OccurredAt = now, Kind = "Expired" });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return true;
    }
}
