using Microsoft.EntityFrameworkCore;
using Server.Features.Scheduling;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class WeeklyBreaks
{
    public sealed record BreakRequest(string? StartsAt, string? EndsAt);
    public sealed record DayRequest(int? Day, BreakRequest[]? Breaks);
    public sealed record UpdateRequest(Guid Version, DayRequest[]? Days);
    public sealed record BreakResponse(string StartsAt, string EndsAt);
    public sealed record DayResponse(int Day, BreakResponse[] Breaks);
    public sealed record Response(bool IsConfigured, string TimeZone, Guid Version, DayResponse[] Days);
    internal sealed record Value(int Day, int Position, int Start, int End);

    internal static Value[] Validate(Guid version, DayRequest[]? input, WeeklyHours.DayValue[] hours, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (version == Guid.Empty) errors["version"] = ["Güncel çalışma saatleri sürümü gerekli."];
        if (input is not { Length: 7 } || input.Any(item => item is null || item.Day is null or < 0 or > 6) || input.Select(item => item.Day).Distinct().Count() != 7)
        {
            errors["days"] = ["Haftanın yedi günü birer kez gönderilmeli."];
            return [];
        }
        var values = new List<Value>();
        foreach (var day in input)
        {
            var key = $"day{day.Day}Breaks";
            if (day.Breaks is null || day.Breaks.Length > 8) { errors[key] = ["Mola listesi gerekli; bir günde en fazla 8 mola olabilir."]; continue; }
            var work = hours.SingleOrDefault(item => item.Day == day.Day);
            var previousEnd = -1;
            for (var index = 0; index < day.Breaks.Length; index++)
            {
                var start = WeeklyHours.Minute(day.Breaks[index]?.StartsAt);
                var end = WeeklyHours.Minute(day.Breaks[index]?.EndsAt);
                if (start is null || end is null || start >= end || start < previousEnd || work is null || work.IsClosed || work.Start is null || work.End is null || start < work.Start || end > work.End)
                    errors[key] = ["Molalar SS:dd biçiminde, aynı gün mesai içinde, başlangıç sırasıyla ve kesişmeden gönderilmeli."];
                else values.Add(new Value(day.Day.GetValueOrDefault(), index + 1, start.Value, end.Value));
                previousEnd = end ?? previousEnd;
            }
        }
        return values.OrderBy(item => item.Day).ThenBy(item => item.Position).ToArray();
    }

    internal static Response Result(bool configured, Guid version, IReadOnlyList<Value> values) => new(configured, "Europe/Istanbul", version,
        Enumerable.Range(0, 7).Select(day => new DayResponse(day, values.Where(item => item.Day == day)
            .Select(item => new BreakResponse(WeeklyHours.Hour(item.Start)!, WeeklyHours.Hour(item.End)!)).ToArray())).ToArray());

    internal static async Task<Value[]> LoadAsync(AppDbContext db, Guid? memberId, CancellationToken token) => memberId is null
        ? await db.BusinessWeeklyBreaks.AsNoTracking().OrderBy(item => item.Day).ThenBy(item => item.Position)
            .Select(item => new Value(item.Day, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token)
        : await db.StaffWeeklyBreaks.AsNoTracking().Where(item => item.StaffMemberId == memberId).OrderBy(item => item.Day).ThenBy(item => item.Position)
            .Select(item => new Value(item.Day, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token);

    internal static bool WithinHours(IReadOnlyList<Value> breaks, WeeklyHours.DayValue[] hours) => breaks.All(item =>
        hours.Any(day => day.Day == item.Day && !day.IsClosed && day.Start <= item.Start && day.End >= item.End));

    internal static bool Avoids(DateTime start, DateTime end, IEnumerable<Value> breaks)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, AppointmentRules.Istanbul));
        var weekday = AppointmentRules.WeekDay(start);
        return breaks.Where(item => item.Day == weekday).All(item =>
            AppointmentRules.TryUtc(date, item.Start, AppointmentRules.Istanbul, out var first) &&
            AppointmentRules.TryUtc(date, item.End, AppointmentRules.Istanbul, out var last) && !(start < last && first < end));
    }
}
