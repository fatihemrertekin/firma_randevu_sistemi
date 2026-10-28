using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class AppointmentExportEndpoints
{
    private const int MaximumRows = 5000;

    internal static void MapAppointmentExportEndpoints(this RouteGroupBuilder appointments) =>
        appointments.MapGet("/export", ExportAsync);

    private static async Task<IResult> ExportAsync(HttpContext context, AppDbContext db, ILoggerFactory loggerFactory,
        string? from = null, string? to = null, Guid? staffMemberId = null)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ||
            !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) ||
            first == DateOnly.MinValue || last == DateOnly.MaxValue || last < first || last.DayNumber - first.DayNumber >= 31 ||
            staffMemberId == Guid.Empty ||
            !AppointmentRules.TryUtc(first, 0, AppointmentRules.Istanbul, out var start) ||
            !AppointmentRules.TryUtc(last.AddDays(1), 0, AppointmentRules.Istanbul, out var end))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["date"] = ["Başlangıç ve bitiş tarihlerini kontrol edin. En fazla 31 günlük bir aralık seçin."] });

        // Tek SQL snapshot'ı; sınıra takılan sonuç için eksik bir Excel dosyası üretme.
        var rows = await (from appointment in db.Appointments.AsNoTracking()
                          join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
                          where block.StartsAt >= start && block.StartsAt < end &&
                              (staffMemberId == null || block.StaffMemberId == staffMemberId)
                          orderby block.StartsAt, appointment.Id
                          select new AppointmentExcel.Row(block.StartsAt, block.EndsAt, appointment.CustomerName,
                              appointment.CustomerPhone, block.ServiceName, block.StaffName, block.Status))
            .Take(MaximumRows + 1).ToArrayAsync(timeout.Token);
        if (rows.Length > MaximumRows)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["date"] = ["Seçiminizde 5.000'den fazla randevu var. Tarih aralığını daraltın veya bir çalışan seçin."] });

        var file = AppointmentExcel.Create(rows);
        loggerFactory.CreateLogger("AppointmentExport").LogInformation(
            "Appointment workbook prepared. UserId: {UserId}, Rows: {Rows}, CorrelationId: {CorrelationId}",
            context.User.FindFirstValue(ClaimTypes.NameIdentifier), rows.Length, context.TraceIdentifier);
        return Results.File(file, AppointmentExcel.ContentType,
            $"randevular_{first:yyyy-MM-dd}_{last:yyyy-MM-dd}.xlsx");
    }
}
