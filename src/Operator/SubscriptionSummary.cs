using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace SubscriptionOperator;

internal sealed record SubscriptionSummary(bool Recorded, SubscriptionSummary.Period? LatestStartedPeriod,
    string OverdueOutstandingTry, int OverduePeriodCount, bool RestrictionsApplied)
{
    internal sealed record Period(Guid Id, DateOnly StartsOn, DateOnly EndsOn, DateOnly DueOn,
        string AmountTry, string PaidTry, string OutstandingTry, string PaymentState);
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    internal static async Task<SubscriptionSummary> ReadAsync(AppDbContext db, DateOnly today, CancellationToken token)
    {
        var balances = db.SubscriptionPeriods.AsNoTracking().Select(p => new
        {
            p.Id,
            p.StartsOn,
            p.EndsOn,
            p.DueOn,
            p.AmountTry,
            Paid = db.SubscriptionPayments.Where(x => x.PeriodId == p.Id).Sum(x => (decimal?)x.AmountTry) ?? 0m
        });
        var latest = await balances.Where(p => p.StartsOn <= today).OrderByDescending(p => p.StartsOn).FirstOrDefaultAsync(token);
        var overdue = await balances.Where(p => p.StartsOn <= today && p.DueOn < today && p.Paid < p.AmountTry)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Outstanding = g.Sum(p => p.AmountTry - p.Paid) }).SingleOrDefaultAsync(token);
        return new(await db.SubscriptionPeriods.AnyAsync(token), latest is null ? null : new(latest.Id, latest.StartsOn, latest.EndsOn, latest.DueOn,
            Money(latest.AmountTry), Money(latest.Paid), Money(latest.AmountTry - latest.Paid), latest.Paid == latest.AmountTry ? "Paid" : latest.Paid > 0m ? "Partial" : "Unpaid"),
            Money(overdue?.Outstanding ?? 0m), overdue?.Count ?? 0, overdue is not null);
    }
}
