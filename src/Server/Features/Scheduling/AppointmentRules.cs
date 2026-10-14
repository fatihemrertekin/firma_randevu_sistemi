using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Server.Features.Business;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static partial class AppointmentRules
{
    internal static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public static bool TryUtc(DateOnly date, int minute, TimeZoneInfo zone, out DateTime utc)
    {
        utc = default;
        if (minute is < 0 or >= 1440) return false;
        var local = date.ToDateTime(new TimeOnly(minute / 60, minute % 60));
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return false;
        utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return true;
    }

    internal static bool TryStart(string? input, out DateTime start)
    {
        start = default;
        if (input is not { Length: <= 35 } || !OffsetTimestamp().IsMatch(input) ||
            !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        start = parsed.UtcDateTime;
        return start.Ticks % TimeSpan.TicksPerMinute == 0;
    }

    internal static int WeekDay(DateTime utc) => ((int)TimeZoneInfo.ConvertTimeFromUtc(utc, Istanbul).DayOfWeek + 6) % 7;
    internal static bool InWindow(DateTime start, DateTime now, BookingPolicy policy)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, Istanbul);
        return start >= now.AddMinutes(policy.LeadMinutes) && start <= now.AddDays(policy.HorizonDays) &&
            (local.Hour * 60 + local.Minute) % policy.SlotMinutes == 0;
    }

    internal static bool FitsDay(DateTime start, DateTime end, bool closed, int? opens, int? closes)
    {
        if (closed || opens is null || closes is null || end <= start) return false;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, Istanbul));
        return TryUtc(date, opens.Value, Istanbul, out var first) && TryUtc(date, closes.Value, Istanbul, out var last) &&
            start >= first && end <= last;
    }

    internal static async Task<bool> FitsWeekAsync(AppDbContext db, WeeklyHours.DayValue[] desired,
        DateTime now, Guid? memberId, CancellationToken token)
    {
        // Çağıran saat/personel ana kaydını kilitler; rezervasyon aynı kilidi alır.
        var blocks = db.AppointmentTimeBlocks.AsNoTracking().Where(block =>
            (block.Status == "Pending" || block.Status == "Confirmed") && block.EndsAt > now);
        if (memberId is not null) blocks = blocks.Where(block => block.StaffMemberId == memberId);
        await foreach (var block in blocks.Select(block => new { block.StartsAt, block.EndsAt }).AsAsyncEnumerable().WithCancellation(token))
        {
            var day = desired.Single(item => item.Day == WeekDay(block.StartsAt));
            if (!FitsDay(block.StartsAt, block.EndsAt, day.IsClosed, day.Start, day.End)) return false;
        }
        return true;
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z")]
    private static partial Regex OffsetTimestamp();
}
