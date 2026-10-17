using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

internal static class BookingVerificationQuota
{
    internal static async Task<DateTimeOffset?> SendWaitAsync(AppDbContext db, string phone, string ip,
        DateTimeOffset now, CancellationToken token)
    {
        var day = BookingVerificationSecurity.DayStart(now);
        var window = now.AddMinutes(-15);
        var since = window < day ? window : day;
        var events = await db.BookingVerificationQuotaEvents.AsNoTracking()
            .Where(x => x.Kind == "Send" && x.OccurredAt >= since).OrderBy(x => x.OccurredAt).ToArrayAsync(token);
        var waits = new List<DateTimeOffset>();
        void Rolling(IEnumerable<BookingVerificationQuotaEvent> items, int limit)
        {
            var rows = items.Where(x => x.OccurredAt > window).ToArray();
            if (rows.Length >= limit) waits.Add(rows[^limit].OccurredAt.AddMinutes(15));
        }
        void Daily(IEnumerable<BookingVerificationQuotaEvent> items, int limit)
        {
            if (items.Count(x => x.OccurredAt >= day) >= limit) waits.Add(BookingVerificationSecurity.DayEnd(now));
        }
        var phoneEvents = events.Where(x => x.PhoneHash == phone).ToArray();
        var last = phoneEvents.LastOrDefault();
        if (last is not null && last.OccurredAt.AddSeconds(60) > now) waits.Add(last.OccurredAt.AddSeconds(60));
        Rolling(phoneEvents, 3); Daily(phoneEvents, 10);
        var ipEvents = events.Where(x => x.IpHash == ip).ToArray();
        Rolling(ipEvents, 5); Daily(ipEvents, 20);
        Rolling(events, 30); Daily(events, 200);
        return waits.Count == 0 ? null : waits.Max();
    }

    internal static void Event(AppDbContext db, string keyId, string kind, string phone, string ip, DateTimeOffset now) =>
        db.BookingVerificationQuotaEvents.Add(new()
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            Kind = kind,
            PhoneHash = phone,
            IpHash = ip,
            OccurredAt = now,
            RetainUntil = BookingVerificationSecurity.DayEnd(now, 2)
        });

    internal static async Task<DateTimeOffset?> WrongWaitAsync(AppDbContext db, string phone, DateTimeOffset now, CancellationToken token)
    {
        var window = now.AddMinutes(-15);
        var latest = await db.BookingVerificationQuotaEvents.AsNoTracking()
            .Where(x => x.Kind == "Wrong" && x.PhoneHash == phone && x.OccurredAt > window)
            .OrderByDescending(x => x.OccurredAt).Select(x => x.OccurredAt).Take(10).ToArrayAsync(token);
        return latest.Length == 10 ? latest[^1].AddMinutes(15) : null;
    }

    internal static async Task<DateTimeOffset?> ReserveCallAsync(AppDbContext db, DateTimeOffset now, CancellationToken token)
    {
        var local = TimeZoneInfo.ConvertTime(now, AppointmentRules.Istanbul).Date;
        var month = new DateTime(local.Year, local.Month, 1);
        var keys = new[] { "D" + local.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), "M" + local.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture) };
        var ends = new[] { BookingVerificationSecurity.DayEnd(now), BookingVerificationSecurity.CalendarStart(month.AddMonths(1)) };
        var limits = new[] { 200, 2000 };
        var rows = new BookingSmsPeriod[2];
        var waits = new List<DateTimeOffset>();
        for (var i = 0; i < keys.Length; i++)
        {
            var row = await db.BookingSmsPeriods.FindAsync([keys[i]], token);
            if (row is null) { row = new() { Key = keys[i], EndsAt = ends[i] }; db.BookingSmsPeriods.Add(row); }
            rows[i] = row;
            if (row.Attempts >= limits[i]) waits.Add(row.EndsAt);
        }
        if (waits.Count > 0) return waits.Max();
        foreach (var row in rows) row.Attempts++;
        return null;
    }
}
