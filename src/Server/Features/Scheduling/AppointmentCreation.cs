using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

internal sealed record AppointmentCreation(BookingPolicy Policy, bool IsConfigured, StaffMember? Member, ServiceDefinition? Service)
{
    internal static async Task<AppointmentCreation> LockAsync(AppDbContext db, Guid staffId, Guid serviceId, CancellationToken token)
    {
        var policy = await db.BookingPolicies.FromSqlRaw("SELECT * FROM \"BookingPolicies\" WHERE \"Id\" = 1 FOR SHARE").SingleAsync(token);
        var schedule = await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\" = 1 FOR SHARE").SingleAsync(token);
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {staffId} FOR UPDATE").SingleOrDefaultAsync(token);
        var service = await db.ServiceDefinitions.FromSqlInterpolated($"SELECT * FROM \"ServiceDefinitions\" WHERE \"Id\" = {serviceId} FOR SHARE").SingleOrDefaultAsync(token);
        return new(policy, schedule.IsConfigured, member, service);
    }

    internal async Task<(AppointmentTimeBlock? Block, IResult? Error)> PrepareAsync(AppDbContext db, Guid serviceVersion,
        DateTime start, DateTime now, bool pending, CancellationToken token)
    {
        if (Member is null || Member.IsDeleted || Service is null || Service.IsDeleted) return (null, Results.NotFound());
        if (Service.Version != serviceVersion) return (null, Conflict("Hizmet bilgisi değişti. Güncel fiyat ve süreyi yükleyin."));
        if (!Member.IsActive || !Service.IsActive || !IsConfigured ||
            !await db.StaffServiceAssignments.AnyAsync(x => x.StaffMemberId == Member.Id && x.ServiceDefinitionId == Service.Id, token))
            return (null, Conflict("Personel, hizmet veya çalışma saatleri rezervasyona uygun değil."));
        if (!AppointmentRules.InWindow(start, now, Policy) || pending && start <= now)
            return (null, Conflict("Başlangıç rezervasyon aralığına ve saat adımına uygun değil."));
        var end = start.AddMinutes(Service.DurationMinutes);
        var occupiedStart = start.AddMinutes(-Service.BufferBeforeMinutes);
        var occupiedEnd = end.AddMinutes(Service.BufferAfterMinutes);
        var date = AppointmentCalendar.LocalDate(start);
        var business = await AppointmentCalendar.LoadAsync(db, null, date, date, token);
        var staff = await AppointmentCalendar.LoadAsync(db, Member.Id, date, date, token);
        if (!business.Fits(occupiedStart, occupiedEnd, start) || !staff.Fits(occupiedStart, occupiedEnd, start))
            return (null, Conflict("Randevu ve tampon sürelerinin tamamı etkin mesai içinde, mola ve izin dışında olmalı."));
        return (new AppointmentTimeBlock
        {
            StaffMemberId = Member.Id,
            ServiceDefinitionId = Service.Id,
            StaffName = Member.Name,
            ServiceName = Service.Name,
            DurationMinutes = Service.DurationMinutes,
            BufferBeforeMinutes = Service.BufferBeforeMinutes,
            BufferAfterMinutes = Service.BufferAfterMinutes,
            Price = Service.Price,
            Currency = Service.Currency,
            StartsAt = start,
            EndsAt = end,
            OccupiedStartsAt = occupiedStart,
            OccupiedEndsAt = occupiedEnd,
            Status = pending ? "Pending" : "Confirmed",
            PendingExpiresAt = pending ? (now.AddMinutes(30) < start ? now.AddMinutes(30) : start) : null
        }, null);
    }

    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);
}
