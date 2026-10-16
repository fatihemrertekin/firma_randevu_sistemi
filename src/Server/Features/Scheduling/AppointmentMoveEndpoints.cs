using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Features.Business;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class AppointmentMoveEndpoints
{
    public sealed record Request(Guid Version, Guid RequestKey, Guid StaffMemberId, string? StartsAt);
    public sealed record AvailabilityResponse(Guid Version, string TimeZone, DateTime[] StartsAt, AppointmentEndpoints.UnavailableReason[] UnavailableReasons);

    internal static void MapAppointmentMoveEndpoints(this RouteGroupBuilder appointments)
    {
        appointments.MapGet("/{id:guid}/move-availability", AvailabilityAsync);
        appointments.MapPost("/{id:guid}/move", MoveAsync);
    }
    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);

    private static async Task<IResult> AvailabilityAsync(Guid id, Guid staffMemberId, string date, HttpContext context, AppDbContext db, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context); var token = timeout.Token;
        if (id == Guid.Empty || staffMemberId == Guid.Empty || !DateExceptionEndpoints.TryDate(date, out var day))
            return Results.Problem(statusCode: 400, title: "Geçerli randevu, personel ve tarih gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var block = await db.AppointmentTimeBlocks.AsNoTracking().SingleOrDefaultAsync(item => item.AppointmentId == id, token);
        var member = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == staffMemberId && !item.IsDeleted, token);
        if (block is null || member is null) return Results.NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        if (block.Status != "Confirmed" || now >= block.StartsAt) return Conflict("Yalnız başlamamış onaylı randevu taşınabilir.");
        var policy = await db.BookingPolicies.AsNoTracking().SingleAsync(token);
        var localNow = AppointmentCalendar.LocalDate(now);
        if (day < localNow || day > localNow.AddDays(policy.HorizonDays))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = [$"Geçmiş bir gün veya {policy.HorizonDays} günlük rezervasyon ufkunun dışı seçilemez. Tarihi değiştirin."] });
        var service = await db.ServiceDefinitions.AsNoTracking().SingleAsync(item => item.Id == block.ServiceDefinitionId, token);
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(token);
        var version = await db.Appointments.Where(item => item.Id == id).Select(item => item.Version).SingleAsync(token);
        var availability = await AppointmentAvailability.FindAsync(db, member, service, schedule.IsConfigured, day, now, policy,
            block.DurationMinutes, block.BufferBeforeMinutes, block.BufferAfterMinutes, id, token);
        await transaction.CommitAsync(token);
        return Results.Ok(new AvailabilityResponse(version, "Europe/Istanbul", availability.StartsAt, availability.UnavailableReasons));
    }

    private static async Task<IResult> MoveAsync(Guid id, Request request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context); var token = timeout.Token;
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (id == Guid.Empty || request.Version == Guid.Empty || request.RequestKey == Guid.Empty || request.StaffMemberId == Guid.Empty || !AppointmentRules.TryStart(request.StartsAt, out var start))
            return Results.Problem(statusCode: 400, title: "Randevu, güncel sürüm, işlem anahtarı, personel ve saat dilimli tam dakika gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, token); if (owner is null) return Results.Unauthorized();
        var previous = await db.AppointmentMoveCommands.AsNoTracking().SingleOrDefaultAsync(item => item.ActorId == owner.Id && item.RequestKey == request.RequestKey, token);
        if (previous is not null)
            return previous.AppointmentId != id || previous.ExpectedVersion != request.Version || previous.StaffMemberId != request.StaffMemberId || previous.StartsAt != start
                ? Conflict("Aynı taşıma anahtarı farklı bilgilerle kullanılamaz.") : Results.Ok(await AppointmentEndpoints.ResponseAsync(db, id, token));
        var original = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.AppointmentId == id).Select(item => new { item.StaffMemberId, item.ServiceDefinitionId }).SingleOrDefaultAsync(token);
        if (original is null) return Results.NotFound();
        var policy = await db.BookingPolicies.FromSqlRaw("SELECT * FROM \"BookingPolicies\" WHERE \"Id\"=1 FOR SHARE").SingleAsync(token);
        var schedule = await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR SHARE").SingleAsync(token);
        StaffMember? target = null;
        // UUID sırası PostgreSQL tarafından seçilir; karşılıklı taşımalar aynı kilit sırasını kullanır.
        foreach (var memberId in await db.StaffMembers.AsNoTracking().Where(item => item.Id == original.StaffMemberId || item.Id == request.StaffMemberId).OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(token))
        {
            var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={memberId} FOR UPDATE").SingleAsync(token);
            if (member.Id == request.StaffMemberId) target = member;
        }
        if (target is null || target.IsDeleted) return Results.NotFound();
        var service = await db.ServiceDefinitions.FromSqlInterpolated($"SELECT * FROM \"ServiceDefinitions\" WHERE \"Id\"={original.ServiceDefinitionId} FOR SHARE").SingleAsync(token);
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(token);
        var block = await db.AppointmentTimeBlocks.SingleAsync(item => item.AppointmentId == id, token);
        if (appointment.Version != request.Version || block.StaffMemberId != original.StaffMemberId || block.ServiceDefinitionId != original.ServiceDefinitionId) return Conflict("Randevu değişti. Güncel kaydı yükleyin.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (block.Status != "Confirmed" || now >= block.StartsAt) return Conflict("Yalnız başlamamış onaylı randevu taşınabilir.");
        if (!target.IsActive || !service.IsActive || service.IsDeleted || !schedule.IsConfigured ||
            !await db.StaffServiceAssignments.AnyAsync(item => item.StaffMemberId == target.Id && item.ServiceDefinitionId == service.Id, token)) return Conflict("Hedef personel veya hizmet rezervasyona uygun değil.");
        if (!AppointmentRules.InWindow(start, now, policy)) return Conflict("Hedef başlangıç rezervasyon aralığına ve saat adımına uygun değil.");
        var end = start.AddMinutes(block.DurationMinutes); var occupiedStart = start.AddMinutes(-block.BufferBeforeMinutes); var occupiedEnd = end.AddMinutes(block.BufferAfterMinutes);
        var day = AppointmentCalendar.LocalDate(start);
        var business = await AppointmentCalendar.LoadAsync(db, null, day, day, token); var staff = await AppointmentCalendar.LoadAsync(db, target.Id, day, day, token);
        if (!business.Fits(occupiedStart, occupiedEnd, start) || !staff.Fits(occupiedStart, occupiedEnd, start)) return Conflict("Randevu ve tampon süreleri etkin mesai içinde, mola ve izin dışında olmalı.");
        Guid auditId;
        if (block.StaffMemberId == target.Id && block.StartsAt == start)
            auditId = await db.AppointmentAudits.Where(item => item.AppointmentId == id && item.AppointmentVersion == appointment.Version).Select(item => item.Id).SingleAsync(token);
        else
        {
            if (block.StaffMemberId != target.Id) block.StaffName = target.Name;
            block.StaffMemberId = target.Id; block.StartsAt = start; block.EndsAt = end; block.OccupiedStartsAt = occupiedStart; block.OccupiedEndsAt = occupiedEnd;
            appointment.Version = Guid.NewGuid(); auditId = Guid.NewGuid();
            db.AppointmentAudits.Add(new() { Id = auditId, AppointmentId = id, ActorId = owner.Id, AppointmentVersion = appointment.Version, OccurredAt = now, Kind = "Moved" });
        }
        db.AppointmentMoveCommands.Add(new() { ActorId = owner.Id, RequestKey = request.RequestKey, AppointmentId = id, ExpectedVersion = request.Version, StaffMemberId = target.Id, StartsAt = start, AuditId = auditId });
        try { await db.SaveChangesAsync(token); await transaction.CommitAsync(token); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23P01", ConstraintName: "EX_AppointmentTimeBlocks_StaffTime" })
        { return Conflict("Hedef saat başka bir randevu tarafından alındı. Eski randevu korundu."); }
        return Results.Ok(await AppointmentEndpoints.ResponseAsync(db, id, token));
    }
}
