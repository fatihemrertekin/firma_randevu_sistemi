using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

internal static class BookingSmsBudget
{
    internal sealed record Reservation(BookingSmsAttempt? Attempt, DateTimeOffset? WaitUntil = null, bool Warning = false);

    internal static async Task<BookingSmsBudgetPolicy?> PolicyAsync(AppDbContext db, BookingVerificationSecurity security,
        DateTimeOffset now, CancellationToken token, decimal? maximumUnitPriceTry = null)
    {
        if (!security.DispatchEnabled) return null;
        var policy = await db.BookingSmsBudgetPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == security.BudgetApprovalId, token);
        return policy is not null && policy.InstanceId == security.InstanceId && policy.KeyId == security.KeyId &&
            policy.RevokedAt is null && policy.StartsAt <= now && now < policy.EndsAt &&
            (maximumUnitPriceTry ?? security.MaximumUnitPriceTry) > 0 && (maximumUnitPriceTry ?? security.MaximumUnitPriceTry) <= policy.UnitPriceTry
            ? policy : null;
    }

    // Çağıran, OTP kilidini ve aynı transaction'ı tutar; ilk rezervasyon kuyrukla birlikte commit edilir.
    internal static async Task<Reservation> ReserveAsync(AppDbContext db, BookingVerificationSecurity security,
        Guid jobId, int number, DateTimeOffset now, CancellationToken token, string purpose = "Otp", decimal? maximumUnitPriceTry = null)
    {
        var policy = await PolicyAsync(db, security, now, token, maximumUnitPriceTry);
        if (policy is null) return new(null);
        var attempt = await db.BookingSmsAttempts.SingleOrDefaultAsync(x => x.JobId == jobId && x.Number == number, token);
        if (await db.BookingSmsAttempts.AnyAsync(x => x.JobId == jobId && x.Purpose != purpose, token)) return new(null);
        if (attempt is not null && (attempt.PolicyId != policy.Id || attempt.StartedAt is not null || attempt.Outcome != "Reserved" || attempt.ReservedTry != policy.UnitPriceTry))
            return new(null);
        if (number > 1 && !await db.BookingSmsAttempts.AnyAsync(x => x.JobId == jobId && x.Number == 1 && x.StartedAt != null, token))
            return new(null);
        var day = BookingVerificationSecurity.DayStart(now);
        var dayEnd = BookingVerificationSecurity.DayEnd(now);
        var local = TimeZoneInfo.ConvertTime(now, AppointmentRules.Istanbul).Date;
        var month = BookingVerificationSecurity.CalendarStart(new DateTime(local.Year, local.Month, 1));
        var monthEnd = BookingVerificationSecurity.CalendarStart(new DateTime(local.Year, local.Month, 1).AddMonths(1));
        var rows = await db.BookingSmsAttempts.AsNoTracking()
            .Where(x => x.Outcome != "Released" && (x.PolicyId == policy.Id || x.ReservedAt >= month))
            .Where(x => x.JobId != jobId || x.Number != number).ToArrayAsync(token);
        var daily = rows.Where(x => x.ReservedAt >= day && x.ReservedAt < dayEnd).ToArray();
        var monthly = rows.Where(x => x.ReservedAt >= month && x.ReservedAt < monthEnd).ToArray();
        var total = rows.Where(x => x.PolicyId == policy.Id).ToArray();
        var waits = new List<DateTimeOffset>();
        void Limit(BookingSmsAttempt[] items, int calls, decimal amount, DateTimeOffset until)
        {
            if (items.Length >= calls || items.Sum(x => x.ReservedTry) + policy.UnitPriceTry > amount) waits.Add(until);
        }
        Limit(daily, policy.DailyCalls, policy.DailyTry, dayEnd);
        Limit(monthly, policy.MonthlyCalls, policy.MonthlyTry, monthEnd);
        Limit(total, policy.TotalCalls, policy.TotalTry, policy.EndsAt);
        if (waits.Count > 0) return new(null, waits.Max());
        if (attempt is null)
        {
            attempt = new() { JobId = jobId, Number = number, PolicyId = policy.Id, ReservedTry = policy.UnitPriceTry, Outcome = "Reserved", Purpose = purpose };
            db.BookingSmsAttempts.Add(attempt);
        }
        // Gece kuyruğa alınan, henüz çağrılmamış iş gerçek çağrı gününe/ayına yeniden ayrılır.
        attempt.ReservedAt = now;
        attempt.RetainUntil = new[] { attempt.RetainUntil, now.AddDays(30), monthEnd.AddDays(1), policy.EndsAt.AddDays(30) }.Max();
        var warning = daily.Length + 1 >= policy.DailyCalls * 0.8m || monthly.Length + 1 >= policy.MonthlyCalls * 0.8m ||
            total.Length + 1 >= policy.TotalCalls * 0.8m || daily.Sum(x => x.ReservedTry) + policy.UnitPriceTry >= policy.DailyTry * 0.8m ||
            monthly.Sum(x => x.ReservedTry) + policy.UnitPriceTry >= policy.MonthlyTry * 0.8m ||
            total.Sum(x => x.ReservedTry) + policy.UnitPriceTry >= policy.TotalTry * 0.8m;
        return new(attempt, Warning: warning);
    }

    internal static Task<int> ReleaseAsync(AppDbContext db, IQueryable<Guid> jobs, CancellationToken token) =>
        db.BookingSmsAttempts.Where(x => x.Purpose == "Otp" && jobs.Contains(x.JobId) && x.StartedAt == null && x.Outcome == "Reserved")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Outcome, "Released"), token);

    internal static async Task MaintainAsync(AppDbContext db, DateTimeOffset now, CancellationToken token)
    {
        var inactive = db.BookingSmsJobs.Where(j => j.Status != "Pending" && j.Status != "Processing" || j.ExpiresAt <= now).Select(j => j.Id);
        await ReleaseAsync(db, inactive, token);
        await db.BookingSmsAttempts.Where(a => a.Purpose == "Reminder" && a.StartedAt == null && a.Outcome == "Reserved" &&
            !db.AppointmentReminderJobs.Any(j => j.Id == a.JobId && (j.Status == "Pending" || j.Status == "Processing") && j.Deadline > now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Outcome, "Released"), token);
        await db.BookingSmsAttempts.Where(a => a.Outcome == "Started" &&
            (a.Purpose == "Otp" && !db.BookingSmsJobs.Any(j => j.Id == a.JobId && j.Status == "Processing" && j.LeaseId == a.LeaseId && j.LeaseUntil > now) ||
             a.Purpose == "Reminder" && !db.AppointmentReminderJobs.Any(j => j.Id == a.JobId && j.Status == "Processing" && j.LeaseId == a.LeaseId && j.LeaseUntil > now)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Outcome, "Unknown"), token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "BookingSmsAttempts" WHERE ("JobId", "Number") IN
            (SELECT "JobId", "Number" FROM "BookingSmsAttempts" WHERE "RetainUntil" <= {now} ORDER BY "RetainUntil" LIMIT 100)
            """, token);
    }
}
