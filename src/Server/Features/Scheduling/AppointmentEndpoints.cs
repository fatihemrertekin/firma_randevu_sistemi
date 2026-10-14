using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class AppointmentEndpoints
{
    public sealed record CreateRequest(Guid RequestKey, Guid ServiceId, Guid ServiceVersion, Guid StaffMemberId,
        string? StartsAt, string? CustomerName, string? CustomerPhone);
    public sealed record AppointmentResponse(Guid Id, Guid Version, Guid StaffMemberId, string StaffName,
        Guid ServiceId, string ServiceName, int DurationMinutes, decimal Price, string Currency,
        DateTime StartsAt, DateTime EndsAt, string Status, string CustomerName, string CustomerPhone,
        int BufferBeforeMinutes, int BufferAfterMinutes, DateTime OccupiedStartsAt, DateTime OccupiedEndsAt);
    public sealed record AvailabilityResponse(string TimeZone, Guid ServiceVersion, DateTime[] StartsAt);

    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var appointments = app.MapGroup("/api/appointments").RequireAuthorization("Owner").RequireRateLimiting("appointments");
        appointments.MapGet("/availability", AvailabilityAsync);
        appointments.MapGet("/{id:guid}", ReadAsync);
        appointments.MapPost("/", CreateAsync);
    }

    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }

    private static Task<AppointmentResponse?> ResponseAsync(AppDbContext db, Guid id, CancellationToken token) =>
        (from appointment in db.Appointments.AsNoTracking()
         join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
         where appointment.Id == id
         select new AppointmentResponse(appointment.Id, appointment.Version, block.StaffMemberId, block.StaffName,
             block.ServiceDefinitionId, block.ServiceName, block.DurationMinutes, block.Price, block.Currency,
             block.StartsAt, block.EndsAt, block.Status, appointment.CustomerName, appointment.CustomerPhone,
             block.BufferBeforeMinutes, block.BufferAfterMinutes, block.OccupiedStartsAt, block.OccupiedEndsAt)).SingleOrDefaultAsync(token);

    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        // Bir kurulum tek firmadır; MFA Owner bu firmanın randevularını okuyabilir.
        var response = await ResponseAsync(db, id, timeout.Token);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }

    private static async Task<IResult> AvailabilityAsync(Guid serviceId, Guid staffMemberId, string date,
        HttpContext context, AppDbContext db, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (serviceId == Guid.Empty || staffMemberId == Guid.Empty ||
            !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Results.Problem(statusCode: 400, title: "Geçerli hizmet, personel ve tarih gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var policy = await db.BookingPolicies.AsNoTracking().SingleAsync(timeout.Token);
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(timeout.Token);
        var member = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == staffMemberId && !item.IsDeleted, timeout.Token);
        var service = await db.ServiceDefinitions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == serviceId && !item.IsDeleted, timeout.Token);
        if (member is null || service is null) return Results.NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var localNow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, AppointmentRules.Istanbul));
        if (day < localNow || day > localNow.AddDays(policy.HorizonDays))
            return Results.Problem(statusCode: 400, title: "Tarih rezervasyon aralığında olmalı.");
        var starts = new List<DateTime>();
        if (schedule.IsConfigured && member.IsActive && service.IsActive &&
            await db.StaffServiceAssignments.AnyAsync(item => item.StaffMemberId == member.Id && item.ServiceDefinitionId == service.Id, timeout.Token))
        {
            var weekday = ((int)day.DayOfWeek + 6) % 7;
            var businessDay = await db.BusinessOpeningDays.AsNoTracking().SingleOrDefaultAsync(item => item.Day == weekday, timeout.Token);
            var staffDay = await db.StaffWorkingDays.AsNoTracking().SingleOrDefaultAsync(item => item.StaffMemberId == member.Id && item.Day == weekday, timeout.Token);
            if (businessDay is not null && staffDay is not null)
            {
                var first = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
                var last = first.AddDays(3);
                var occupied = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.StaffMemberId == member.Id &&
                    item.Status != "Cancelled" && item.OccupiedStartsAt < last && item.OccupiedEndsAt > first)
                    .Select(item => new { item.OccupiedStartsAt, item.OccupiedEndsAt }).ToArrayAsync(timeout.Token);
                for (var minute = 0; minute < 1440; minute += policy.SlotMinutes)
                {
                    if (!AppointmentRules.TryUtc(day, minute, AppointmentRules.Istanbul, out var start) || !AppointmentRules.InWindow(start, now, policy)) continue;
                    var end = start.AddMinutes(service.DurationMinutes);
                    var occupiedStart = start.AddMinutes(-service.BufferBeforeMinutes);
                    var occupiedEnd = end.AddMinutes(service.BufferAfterMinutes);
                    if (AppointmentRules.FitsDay(occupiedStart, occupiedEnd, businessDay.IsClosed, businessDay.OpensAtMinute, businessDay.ClosesAtMinute) &&
                        AppointmentRules.FitsDay(occupiedStart, occupiedEnd, staffDay.IsClosed, staffDay.OpensAtMinute, staffDay.ClosesAtMinute) &&
                        !occupied.Any(item => item.OccupiedStartsAt < occupiedEnd && occupiedStart < item.OccupiedEndsAt)) starts.Add(start);
                }
            }
        }
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new AvailabilityResponse("Europe/Istanbul", service.Version, starts.ToArray()));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var name = request.CustomerName?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (request.RequestKey == Guid.Empty) errors["requestKey"] = ["İşlem anahtarı gerekli."];
        if (request.ServiceId == Guid.Empty || request.ServiceVersion == Guid.Empty) errors["serviceId"] = ["Güncel hizmet seçimi gerekli."];
        if (request.StaffMemberId == Guid.Empty) errors["staffMemberId"] = ["Personel seçimi gerekli."];
        if (name is not { Length: >= 2 and <= 100 } || name.Any(char.IsControl)) errors["customerName"] = ["Ad 2–100 karakter olmalı."];
        if (!TurkishPhone.TryNormalize(request.CustomerPhone, out var phone, out var phoneError)) errors["customerPhone"] = [phoneError];
        if (!AppointmentRules.TryStart(request.StartsAt, out var start)) errors["startsAt"] = ["Saat dilimi içeren, tam dakikalı geçerli başlangıç gerekli."];
        if (errors.Count > 0 || name is null) return Results.ValidationProblem(errors);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            request.ServiceId,
            request.ServiceVersion,
            request.StaffMemberId,
            StartsAt = start,
            CustomerName = name,
            CustomerPhone = phone
        }))));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var previous = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(item => item.ActorId == owner.Id && item.RequestKey == request.RequestKey, timeout.Token);
        if (previous is not null)
            return previous.RequestHash != hash ? Conflict("Aynı işlem anahtarı farklı randevu bilgisiyle kullanılamaz.") :
                Results.Ok(await ResponseAsync(db, previous.Id, timeout.Token));
        var policy = await db.BookingPolicies.FromSqlRaw("SELECT * FROM \"BookingPolicies\" WHERE \"Id\" = 1 FOR SHARE").SingleAsync(timeout.Token);
        var schedule = await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\" = 1 FOR SHARE").SingleAsync(timeout.Token);
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {request.StaffMemberId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        var service = await db.ServiceDefinitions.FromSqlInterpolated($"SELECT * FROM \"ServiceDefinitions\" WHERE \"Id\" = {request.ServiceId} FOR SHARE").SingleOrDefaultAsync(timeout.Token);
        if (member is null || member.IsDeleted || service is null || service.IsDeleted) return Results.NotFound();
        if (service.Version != request.ServiceVersion) return Conflict("Hizmet bilgisi değişti. Güncel fiyat ve süreyi yükleyin.");
        if (!member.IsActive || !service.IsActive || !schedule.IsConfigured ||
            !await db.StaffServiceAssignments.AnyAsync(item => item.StaffMemberId == member.Id && item.ServiceDefinitionId == service.Id, timeout.Token))
            return Conflict("Personel, hizmet veya çalışma saatleri rezervasyona uygun değil.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (!AppointmentRules.InWindow(start, now, policy)) return Conflict("Başlangıç rezervasyon aralığına ve saat adımına uygun değil.");
        var end = start.AddMinutes(service.DurationMinutes);
        var occupiedStart = start.AddMinutes(-service.BufferBeforeMinutes);
        var occupiedEnd = end.AddMinutes(service.BufferAfterMinutes);
        var weekday = AppointmentRules.WeekDay(start);
        var businessDay = await db.BusinessOpeningDays.SingleOrDefaultAsync(item => item.Day == weekday, timeout.Token);
        var staffDay = await db.StaffWorkingDays.SingleOrDefaultAsync(item => item.StaffMemberId == member.Id && item.Day == weekday, timeout.Token);
        if (businessDay is null || staffDay is null ||
            !AppointmentRules.FitsDay(occupiedStart, occupiedEnd, businessDay.IsClosed, businessDay.OpensAtMinute, businessDay.ClosesAtMinute) ||
            !AppointmentRules.FitsDay(occupiedStart, occupiedEnd, staffDay.IsClosed, staffDay.OpensAtMinute, staffDay.ClosesAtMinute))
            return Conflict("Randevu ve tampon sürelerinin tamamı işletme ve personel mesaisinde olmalı.");
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            ActorId = owner.Id,
            RequestKey = request.RequestKey,
            RequestHash = hash,
            CustomerName = name,
            CustomerPhone = phone,
            CreatedAt = now,
            Version = Guid.NewGuid()
        };
        var block = new AppointmentTimeBlock
        {
            AppointmentId = appointment.Id,
            StaffMemberId = member.Id,
            ServiceDefinitionId = service.Id,
            StaffName = member.Name,
            ServiceName = service.Name,
            DurationMinutes = service.DurationMinutes,
            BufferBeforeMinutes = service.BufferBeforeMinutes,
            BufferAfterMinutes = service.BufferAfterMinutes,
            OccupiedStartsAt = occupiedStart,
            OccupiedEndsAt = occupiedEnd,
            Price = service.Price,
            Currency = service.Currency,
            StartsAt = start,
            EndsAt = end,
            Status = "Confirmed"
        };
        db.Appointments.Add(appointment);
        db.AppointmentTimeBlocks.Add(block);
        db.AppointmentAudits.Add(new AppointmentAudit
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            ActorId = owner.Id,
            AppointmentVersion = appointment.Version,
            OccurredAt = now
        });
        try
        {
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: "23P01", ConstraintName: "EX_AppointmentTimeBlocks_StaffTime" })
        {
            return Conflict("Bu saat başka bir randevu tarafından alındı. Uygun saatleri yeniden yükleyin.");
        }
        return Results.Created($"/api/appointments/{appointment.Id}", new AppointmentResponse(appointment.Id, appointment.Version,
            member.Id, block.StaffName, service.Id, block.ServiceName, block.DurationMinutes, block.Price, block.Currency,
            start, end, block.Status, appointment.CustomerName, appointment.CustomerPhone,
            block.BufferBeforeMinutes, block.BufferAfterMinutes, block.OccupiedStartsAt, block.OccupiedEndsAt));
    }

    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);
}
