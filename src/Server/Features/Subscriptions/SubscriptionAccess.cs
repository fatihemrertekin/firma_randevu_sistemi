using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Subscriptions;

public static class SubscriptionAccess
{
    // Operatörün mali yazımıyla işletme yazımlarının kabul sırasını aynı transaction kilidi belirler.
    public const int LedgerLock = 70703;
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
        clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    public static Task<bool> IsRestrictedAsync(AppDbContext db, TimeProvider clock, CancellationToken token)
    {
        var today = Today(clock);
        return db.SubscriptionPeriods.AsNoTracking().AnyAsync(period => period.StartsOn <= today && period.DueOn < today &&
            period.AmountTry > (db.SubscriptionPayments.Where(payment => payment.PeriodId == period.Id)
                .Sum(payment => (decimal?)payment.AmountTry) ?? 0m), token);
    }

    internal static async Task EnsureWritableAsync(HttpContext context, AppDbContext db, CancellationToken token)
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("Abonelik yazma kontrolü ReadCommitted transaction içinde çalışmalı.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({LedgerLock})", timeout.Token);
            if (await IsRestrictedAsync(db, context.RequestServices.GetRequiredService<TimeProvider>(), timeout.Token))
                throw new SubscriptionAccessException(false);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException ||
            exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        { throw new SubscriptionAccessException(true); }
    }

    internal static async Task HandleAsync(HttpContext context, RequestDelegate next)
    {
        try { await next(context); }
        catch (SubscriptionAccessException exception)
        {
            context.Response.Headers.CacheControl = "no-store";
            await Results.Problem(statusCode: exception.Unavailable ? 503 : 403,
                title: context.Request.Path.StartsWithSegments("/api/public-booking")
                    ? "İşletme şu anda yeni çevrim içi randevu alamıyor. İşletmeyle iletişime geçin."
                    : exception.Unavailable
                    ? "Abonelik durumu doğrulanamadı; işlem uygulanmadı. Bağlantıyı kontrol edip yeniden deneyin."
                    : "Vadesi geçmiş abonelik borcu nedeniyle yalnız görüntüleme yapılabilir. Ödeme durumunuz için hizmet sağlayıcınızla iletişime geçin.",
                extensions: new Dictionary<string, object?> { ["code"] = exception.Unavailable ? "subscription_unavailable" : "subscription_read_only" })
                .ExecuteAsync(context);
        }
    }

    private sealed class SubscriptionAccessException(bool unavailable) : Exception
    {
        internal bool Unavailable { get; } = unavailable;
    }
}
