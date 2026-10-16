using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

internal static class AppointmentAvailability
{
    internal sealed record Result(DateTime[] StartsAt, AppointmentEndpoints.UnavailableReason[] UnavailableReasons);

    internal static async Task<Result> FindAsync(AppDbContext db, StaffMember member, ServiceDefinition service,
        bool configured, DateOnly day, DateTime now, BookingPolicy policy, int duration, int before, int after,
        Guid? exclude, CancellationToken token)
    {
        var reasons = new List<AppointmentEndpoints.UnavailableReason>();
        if (!member.IsActive) reasons.Add(new("StaffInactive", "Seçili çalışan pasif. Personel bölümünden durumunu kontrol edin veya başka çalışan seçin."));
        if (!service.IsActive || service.IsDeleted) reasons.Add(new("ServiceInactive", "Bu hizmet kullanıma kapalı. Hizmetler bölümünden durumunu kontrol edin veya başka hizmet seçin."));
        if (!await db.StaffServiceAssignments.AnyAsync(item => item.StaffMemberId == member.Id && item.ServiceDefinitionId == service.Id, token))
            reasons.Add(new("ServiceNotAssigned", "Bu hizmet seçili çalışana atanmamış. Personel → Hizmetler bölümünde hizmeti seçip kaydedin veya başka çalışan seçin."));

        var business = await AppointmentCalendar.LoadAsync(db, null, day, day, token);
        var staff = await AppointmentCalendar.LoadAsync(db, member.Id, day, day, token);
        AddDayReason(business.DayAt(day), configured, "Business", "İşletme", reasons);
        AddDayReason(staff.DayAt(day), true, "Staff", "Çalışan", reasons);
        if (reasons.Count > 0) return new([], reasons.ToArray());

        var first = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1); var last = first.AddDays(3);
        var occupied = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.StaffMemberId == member.Id &&
            (exclude == null || item.AppointmentId != exclude) && item.Status != "Cancelled" && item.OccupiedStartsAt < last && item.OccupiedEndsAt > first)
            .Select(item => new { item.OccupiedStartsAt, item.OccupiedEndsAt }).ToArrayAsync(token);
        var starts = new List<DateTime>();
        var inWindow = 0; var withinHours = 0; var outsideBreaks = 0; var outsideLeave = 0; var hasWorkingTime = false;
        for (var minute = 0; minute < 1440; minute += policy.SlotMinutes)
        {
            if (!AppointmentRules.TryUtc(day, minute, AppointmentRules.Istanbul, out var start)) continue;
            var occupiedStart = start.AddMinutes(-before); var occupiedEnd = start.AddMinutes(duration + after);
            var fitsHours = business.FitsHours(occupiedStart, occupiedEnd, start) && staff.FitsHours(occupiedStart, occupiedEnd, start);
            hasWorkingTime |= fitsHours;
            if (!AppointmentRules.InWindow(start, now, policy)) continue;
            inWindow++;
            if (!fitsHours) continue;
            withinHours++;
            if (!business.AvoidsBreaks(occupiedStart, occupiedEnd, start) || !staff.AvoidsBreaks(occupiedStart, occupiedEnd, start)) continue;
            outsideBreaks++;
            if (!staff.AvoidsLeave(occupiedStart, occupiedEnd)) continue;
            outsideLeave++;
            if (!occupied.Any(item => item.OccupiedStartsAt < occupiedEnd && occupiedStart < item.OccupiedEndsAt)) starts.Add(start);
        }
        if (starts.Count > 0) return new(starts.ToArray(), []);

        var reason = inWindow == 0 || withinHours == 0 && hasWorkingTime
            ? new AppointmentEndpoints.UnavailableReason("BookingWindow", "Minimum ön süre ve rezervasyon ufku nedeniyle bu gün için başlangıç saati kalmadı. Başka bir gün seçin veya Rezervasyon ayarlarını kontrol edin.")
            : withinHours == 0
                ? new("WorkingTime", $"{duration} dk hizmet ve toplam {before + after} dk tampon, başlangıç kurallarına uyan ortak işletme/çalışan mesaisine sığmıyor. Başka bir gün seçin veya çalışma saatlerini kontrol edin.")
                : outsideBreaks == 0
                    ? new("Breaks", "İşletme veya çalışan molaları nedeniyle hizmet ve tamponların sığacağı kesintisiz aralık kalmadı. Başka gün/çalışan seçin veya molaları kontrol edin.")
                    : outsideLeave == 0
                        ? new("Leave", "Çalışanın izinleri nedeniyle bu hizmet için uygun başlangıç saati kalmadı. Başka bir gün veya çalışan seçin.")
                        : new("Occupied", "Mesai, mola, izin ve başlangıç kurallarına uyan aralıklarda diğer randevular nedeniyle bu hizmete yeterli boşluk kalmadı. Başka bir gün veya çalışan seçin.");
        return new([], [reason]);
    }

    private static void AddDayReason(AppointmentCalendar.Day? day, bool configured, string code, string name,
        List<AppointmentEndpoints.UnavailableReason> reasons)
    {
        if (!configured || day is null || !day.IsClosed && (day.Start is null || day.End is null))
            reasons.Add(new(code + "HoursMissing", $"{name} çalışma saatleri eksik. İşletme saatleri veya Personel → Saatler bölümünden mesaiyi tamamlayın."));
        else if (day.IsClosed)
            reasons.Add(new(code + "Closed", $"{name} seçili tarihte kapalı. Başka bir gün seçin veya ilgili günün çalışma saatlerini kontrol edin."));
    }
}
