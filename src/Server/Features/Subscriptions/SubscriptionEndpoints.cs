using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Subscriptions;

public static class SubscriptionEndpoints
{
    public sealed record PeriodResponse(Guid Id, DateOnly StartsOn, DateOnly EndsOn, DateOnly DueOn,
        string AmountTry, string PaidTry, string OutstandingTry, string PaymentState, bool Overdue);
    public sealed record SubscriptionResponse(DateOnly AsOf, string TimeZone, string Currency, bool Recorded,
        bool RestrictionsApplied, PeriodResponse? CurrentPeriod, PeriodResponse[] Periods,
        int Page, int PageSize, int TotalCount, bool HasMore);
    private sealed record Balance(Guid Id, DateOnly StartsOn, DateOnly EndsOn, DateOnly DueOn, decimal AmountTry, decimal PaidTry);

    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/subscription", ReadAsync).RequireAuthorization("Owner").RequireRateLimiting("subscription");
        app.MapGet("/api/subscription/access", AccessAsync).RequireAuthorization("PasswordChange").RequireRateLimiting("subscription");
    }

    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        int page = 1, int pageSize = 12)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 24 ||
            context.Request.Query.Keys.Any(key => key is not ("page" or "pageSize")))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["page"] = ["Dönem sayfasını kontrol edin. Sayfada 1–24 kayıt seçebilirsiniz; firma seçimi bu uçta yapılamaz."] });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var today = SubscriptionAccess.Today(clock);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", timeout.Token);
        var query = db.SubscriptionPeriods.AsNoTracking();
        Expression<Func<SubscriptionPeriod, Balance>> select = period => new Balance(period.Id,
            period.StartsOn, period.EndsOn, period.DueOn, period.AmountTry,
            db.SubscriptionPayments.Where(payment => payment.PeriodId == period.Id).Sum(payment => (decimal?)payment.AmountTry) ?? 0m);
        var count = await query.CountAsync(timeout.Token);
        var current = await query.Where(period => period.StartsOn <= today && period.EndsOn > today).Select(select).SingleOrDefaultAsync(timeout.Token);
        var actualPage = Math.Min(page, Math.Max(1, (count + pageSize - 1) / pageSize));
        var periods = await query.OrderByDescending(period => period.StartsOn).ThenBy(period => period.Id)
            .Skip((actualPage - 1) * pageSize).Take(pageSize).Select(select).ToArrayAsync(timeout.Token);
        var restricted = await SubscriptionAccess.IsRestrictedAsync(db, clock, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new SubscriptionResponse(today, "Europe/Istanbul", "TRY", count > 0, restricted,
            current is null ? null : ToResponse(current, today), periods.Select(period => ToResponse(period, today)).ToArray(),
            actualPage, pageSize, count, actualPage * pageSize < count));
    }

    private static async Task<IResult> AccessAsync(HttpContext context, AppDbContext db, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { return Results.Ok(new { restrictionsApplied = await SubscriptionAccess.IsRestrictedAsync(db, clock, timeout.Token) }); }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException ||
            exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "Abonelik durumu doğrulanamadı. Yeniden deneyin."); }
    }

    private static PeriodResponse ToResponse(Balance period, DateOnly today) => new(period.Id,
        period.StartsOn, period.EndsOn, period.DueOn, Money(period.AmountTry), Money(period.PaidTry), Money(period.AmountTry - period.PaidTry),
        period.PaidTry == period.AmountTry ? "Paid" : period.PaidTry > 0m ? "Partial" : "Unpaid",
        period.PaidTry < period.AmountTry && period.DueOn < today);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
