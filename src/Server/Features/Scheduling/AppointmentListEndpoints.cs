using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class AppointmentListEndpoints
{
    public sealed record AppointmentRow(Guid Id, Guid Version, Guid StaffMemberId, string StaffName,
        string ServiceName, DateTime StartsAt, DateTime EndsAt, string Status, string CustomerName, DateTime? PendingExpiresAt);
    public sealed record AppointmentPage(AppointmentRow[] Items, int Page, int PageSize, int TotalCount, bool HasMore,
        string Date, string TimeZone);
    public sealed record WeekDay(string Date, int TotalCount);
    public sealed record AppointmentWeekPage(AppointmentRow[] Items, int Page, int PageSize, int TotalCount, bool HasMore,
        string Date, string TimeZone, string WeekStart, string WeekEndExclusive, WeekDay[] Days);

    internal static void MapAppointmentListEndpoints(this RouteGroupBuilder appointments)
    {
        appointments.MapGet("/", ListAsync);
        appointments.MapGet("/week", WeekAsync);
    }

    private static Task<IResult> ListAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        string? date = null, Guid? staffMemberId = null, int page = 1, int pageSize = 20) =>
        ReadAsync(context, db, clock, date, staffMemberId, page, pageSize, false);

    private static Task<IResult> WeekAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        string? date = null, Guid? staffMemberId = null, int page = 1, int pageSize = 10) =>
        ReadAsync(context, db, clock, date, staffMemberId, page, pageSize, true);

    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        string? date, Guid? staffMemberId, int page, int pageSize, bool weekly)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, AppointmentRules.Istanbul));
        if (date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ||
            day == DateOnly.MinValue || day == DateOnly.MaxValue || staffMemberId == Guid.Empty ||
            page is < 1 or > 10000 || pageSize is < 1 or > 50 ||
            !AppointmentRules.TryUtc(day, 0, AppointmentRules.Istanbul, out var start) ||
            !AppointmentRules.TryUtc(day.AddDays(1), 0, AppointmentRules.Istanbul, out var end))
            return Results.Problem(statusCode: 400, title: "Geçerli tarih, çalışan ve sayfa bilgisi gerekli; sayfa boyutu 1–50 olmalı.");

        var weekStartNumber = day.DayNumber - ((int)day.DayOfWeek + 6) % 7;
        var weekStart = day;
        var boundaries = new DateTime[8];
        if (weekly)
        {
            if (weekStartNumber <= DateOnly.MinValue.DayNumber || weekStartNumber > DateOnly.MaxValue.DayNumber - 7)
                return Results.Problem(statusCode: 400, title: "Seçili haftanın bütün günleri desteklenen tarih aralığında olmalı.");
            weekStart = DateOnly.FromDayNumber(weekStartNumber);
            for (var index = 0; index < boundaries.Length; index++)
                if (!AppointmentRules.TryUtc(weekStart.AddDays(index), 0, AppointmentRules.Istanbul, out boundaries[index]))
                    return Results.Problem(statusCode: 400, title: "Seçili haftanın saat sınırları hesaplanamadı.");
        }
        var rangeStart = weekly ? boundaries[0] : start;
        var rangeEnd = weekly ? boundaries[7] : end;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        // Tarihsel snapshot okunur; pasif veya silinmiş tanım geçmiş randevuyu gizlemez.
        var query = from appointment in db.Appointments.AsNoTracking()
                    join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
                    where block.StartsAt >= rangeStart && block.StartsAt < rangeEnd &&
                        (staffMemberId == null || block.StaffMemberId == staffMemberId)
                    select new { appointment, block };
        int[]? counts = null;
        if (weekly)
        {
            // Sabit yedi aggregate; haftanın randevularını veya gün başına ayrı sorguları yükleme.
            counts = await query.GroupBy(item => 1).Select(group => new[]
            {
                group.Count(item => item.block.StartsAt < boundaries[1]),
                group.Count(item => item.block.StartsAt >= boundaries[1] && item.block.StartsAt < boundaries[2]),
                group.Count(item => item.block.StartsAt >= boundaries[2] && item.block.StartsAt < boundaries[3]),
                group.Count(item => item.block.StartsAt >= boundaries[3] && item.block.StartsAt < boundaries[4]),
                group.Count(item => item.block.StartsAt >= boundaries[4] && item.block.StartsAt < boundaries[5]),
                group.Count(item => item.block.StartsAt >= boundaries[5] && item.block.StartsAt < boundaries[6]),
                group.Count(item => item.block.StartsAt >= boundaries[6])
            }).SingleOrDefaultAsync(timeout.Token) ?? new int[7];
        }
        var selected = weekly ? query.Where(item => item.block.StartsAt >= start && item.block.StartsAt < end) : query;
        var total = counts is null ? await selected.CountAsync(timeout.Token) : counts[day.DayNumber - weekStart.DayNumber];
        page = Math.Min(page, Math.Max(1, (total + pageSize - 1) / pageSize));
        var rows = await selected.OrderBy(item => item.block.StartsAt).ThenBy(item => item.appointment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AppointmentRow(item.appointment.Id, item.appointment.Version, item.block.StaffMemberId, item.block.StaffName,
                item.block.ServiceName, item.block.StartsAt, item.block.EndsAt, item.block.Status, item.appointment.CustomerName, item.block.PendingExpiresAt))
            .ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        if (counts is not null)
        {
            var days = Enumerable.Range(0, 7).Select(index => new WeekDay(
                weekStart.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), counts[index])).ToArray();
            return Results.Ok(new AppointmentWeekPage(rows, page, pageSize, total, page * pageSize < total,
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Europe/Istanbul",
                days[0].Date, weekStart.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), days));
        }
        return Results.Ok(new AppointmentPage(rows, page, pageSize, total, page * pageSize < total,
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Europe/Istanbul"));
    }
}
