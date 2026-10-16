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

    internal static void MapAppointmentListEndpoints(this RouteGroupBuilder appointments) => appointments.MapGet("/", ListAsync);

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, TimeProvider clock,
        string? date = null, Guid? staffMemberId = null, int page = 1, int pageSize = 20)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, AppointmentRules.Istanbul));
        if (date is not null && !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ||
            day == DateOnly.MinValue || day == DateOnly.MaxValue || staffMemberId == Guid.Empty ||
            page is < 1 or > 10000 || pageSize is < 1 or > 50 ||
            !AppointmentRules.TryUtc(day, 0, AppointmentRules.Istanbul, out var start) ||
            !AppointmentRules.TryUtc(day.AddDays(1), 0, AppointmentRules.Istanbul, out var end))
            return Results.Problem(statusCode: 400, title: "Geçerli tarih, çalışan ve sayfa bilgisi gerekli; sayfa boyutu 1–50 olmalı.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        // Tarihsel snapshot okunur; pasif veya silinmiş tanım geçmiş randevuyu gizlemez.
        var query = from appointment in db.Appointments.AsNoTracking()
                    join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
                    where block.StartsAt >= start && block.StartsAt < end &&
                        (staffMemberId == null || block.StaffMemberId == staffMemberId)
                    select new { appointment, block };
        var total = await query.CountAsync(timeout.Token);
        page = Math.Min(page, Math.Max(1, (total + pageSize - 1) / pageSize));
        var rows = await query.OrderBy(item => item.block.StartsAt).ThenBy(item => item.appointment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new AppointmentRow(item.appointment.Id, item.appointment.Version, item.block.StaffMemberId, item.block.StaffName,
                item.block.ServiceName, item.block.StartsAt, item.block.EndsAt, item.block.Status, item.appointment.CustomerName, item.block.PendingExpiresAt))
            .ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new AppointmentPage(rows, page, pageSize, total, page * pageSize < total,
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Europe/Istanbul"));
    }
}
