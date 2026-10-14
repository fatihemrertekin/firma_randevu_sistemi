using Microsoft.EntityFrameworkCore;
using Server.Features.Business;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

internal sealed class AppointmentCalendar(WeeklyHours.DayValue[] week, IReadOnlyList<WeeklyBreaks.Value> weeklyBreaks)
{
    internal sealed record Day(bool IsClosed, int? Start, int? End, WeeklyBreaks.Value[] Breaks);
    private sealed record ExceptionRow(DateOnly Date, bool IsClosed, int? Start, int? End);
    private sealed record BreakRow(DateOnly Date, int Position, int Start, int End);
    private sealed record LeaveRow(DateTime Start, DateTime End);
    private readonly Dictionary<DateOnly, Day> exceptions = [];
    private LeaveRow[] leave = [];
    internal static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, AppointmentRules.Istanbul));
    internal static int Weekday(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;
    internal void Override(DateOnly date, Day? day)
    {
        if (day is null) exceptions.Remove(date); else exceptions[date] = day;
    }
    internal bool Fits(DateTime start, DateTime end, DateTime serviceStart)
    {
        if (week.Length != 7 || LocalDate(start) != LocalDate(serviceStart)) return false;
        var date = LocalDate(serviceStart); var weekday = Weekday(date);
        var weekly = week.SingleOrDefault(item => item.Day == weekday);
        if (weekly is null) return false;
        var day = exceptions.GetValueOrDefault(date) ?? new Day(weekly.IsClosed, weekly.Start, weekly.End, weeklyBreaks.Where(item => item.Day == weekday).ToArray());
        return AppointmentRules.FitsDay(start, end, day.IsClosed, day.Start, day.End) && WeeklyBreaks.Avoids(start, end, day.Breaks) &&
            !leave.Any(item => start < item.End && item.Start < end);
    }
    internal static async Task<AppointmentCalendar> LoadAsync(AppDbContext db, Guid? memberId, DateOnly from, DateOnly to,
        CancellationToken token, WeeklyHours.DayValue[]? desiredWeek = null, IReadOnlyList<WeeklyBreaks.Value>? desiredBreaks = null)
    {
        var week = desiredWeek ?? (memberId is null
            ? await db.BusinessOpeningDays.AsNoTracking().Select(item => new WeeklyHours.DayValue(item.Day, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).ToArrayAsync(token)
            : await db.StaffWorkingDays.AsNoTracking().Where(item => item.StaffMemberId == memberId).Select(item => new WeeklyHours.DayValue(item.Day, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).ToArrayAsync(token));
        var calendar = new AppointmentCalendar(week, desiredBreaks ?? await WeeklyBreaks.LoadAsync(db, memberId, token));
        var dates = memberId is null
            ? await db.BusinessDateExceptions.AsNoTracking().Where(item => item.Date >= from && item.Date <= to).Select(item => new ExceptionRow(item.Date, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).ToArrayAsync(token)
            : await db.StaffDateExceptions.AsNoTracking().Where(item => item.StaffMemberId == memberId && item.Date >= from && item.Date <= to).Select(item => new ExceptionRow(item.Date, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).ToArrayAsync(token);
        var breaks = memberId is null
            ? await db.BusinessDateBreaks.AsNoTracking().Where(item => item.Date >= from && item.Date <= to).Select(item => new BreakRow(item.Date, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token)
            : await db.StaffDateBreaks.AsNoTracking().Where(item => item.StaffMemberId == memberId && item.Date >= from && item.Date <= to).Select(item => new BreakRow(item.Date, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token);
        var grouped = breaks.ToLookup(item => item.Date);
        foreach (var date in dates) calendar.Override(date.Date, new Day(date.IsClosed, date.Start, date.End,
            grouped[date.Date].OrderBy(item => item.Position).Select(item => new WeeklyBreaks.Value(Weekday(date.Date), item.Position, item.Start, item.End)).ToArray()));
        if (memberId is not null)
        {
            // UTC sınırı bir gün geniş tutulur; yerel günü kesen izin sorgudan düşmez.
            var first = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            first = first == DateTime.MinValue ? first : first.AddDays(-1);
            var last = to == DateOnly.MaxValue ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            calendar.leave = await db.StaffLeaves.AsNoTracking().Where(item => item.StaffMemberId == memberId && !item.IsRemoved && item.StartsAt < last && item.EndsAt > first)
                .Select(item => new LeaveRow(item.StartsAt, item.EndsAt)).ToArrayAsync(token);
        }
        return calendar;
    }
    internal static async Task<bool> FitsAppointmentsAsync(AppDbContext db, Guid? memberId, DateTime now, CancellationToken token,
        WeeklyHours.DayValue[]? week = null, IReadOnlyList<WeeklyBreaks.Value>? breaks = null, DateOnly? date = null, Day? proposed = null)
    {
        var query = db.AppointmentTimeBlocks.AsNoTracking().Where(item => (item.Status == "Pending" || item.Status == "Confirmed") && item.OccupiedEndsAt > now);
        if (memberId is not null) query = query.Where(item => item.StaffMemberId == memberId);
        if (date is not null)
        {
            AppointmentRules.TryUtc(date.Value, 0, AppointmentRules.Istanbul, out var first);
            var last = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            if (date != DateOnly.MaxValue) AppointmentRules.TryUtc(date.Value.AddDays(1), 0, AppointmentRules.Istanbul, out last);
            query = query.Where(item => item.StartsAt >= first && item.StartsAt < last);
        }
        var blocks = await query.Select(item => new { item.StartsAt, item.OccupiedStartsAt, item.OccupiedEndsAt }).ToArrayAsync(token);
        if (blocks.Length == 0) return true;
        var from = blocks.Min(item => LocalDate(item.StartsAt)); var to = blocks.Max(item => LocalDate(item.StartsAt));
        var calendar = await LoadAsync(db, memberId, from, to, token, week, breaks);
        if (date is not null) calendar.Override(date.Value, proposed);
        return blocks.All(item => calendar.Fits(item.OccupiedStartsAt, item.OccupiedEndsAt, item.StartsAt));
    }
}
