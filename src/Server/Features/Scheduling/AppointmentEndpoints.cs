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
using Server.Features.Business;

namespace Server.Features.Scheduling;

public static class AppointmentEndpoints
{
    public sealed record CreateRequest(Guid RequestKey, Guid ServiceId, Guid ServiceVersion, Guid StaffMemberId,
        string? StartsAt, string? CustomerName, string? CustomerPhone);
    public sealed record AppointmentResponse(Guid Id, Guid Version, Guid StaffMemberId, string StaffName,
        Guid ServiceId, string ServiceName, int DurationMinutes, decimal Price, string Currency,
        DateTime StartsAt, DateTime EndsAt, string Status, string CustomerName, string CustomerPhone,
        int BufferBeforeMinutes, int BufferAfterMinutes, DateTime OccupiedStartsAt, DateTime OccupiedEndsAt, DateTime? PendingExpiresAt = null);
    public sealed record UnavailableReason(string Code, string Message);
    public sealed record AvailabilityResponse(string TimeZone, Guid ServiceVersion, DateTime[] StartsAt, UnavailableReason[] UnavailableReasons);

    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var appointments = app.MapGroup("/api/appointments").RequireAuthorization("Owner").RequireRateLimiting("appointments");
        appointments.MapAppointmentListEndpoints();
        appointments.MapGet("/availability", AvailabilityAsync);
        appointments.MapGet("/{id:guid}", ReadAsync);
        appointments.MapPost("/", (CreateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => CreateAsync(request, false, context, csrf, db, users, clock));
        appointments.MapPost("/pending", (CreateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => CreateAsync(request, true, context, csrf, db, users, clock));
        appointments.MapAppointmentStatusEndpoints();
        appointments.MapAppointmentMoveEndpoints();
    }

    internal static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }

    internal static Task<AppointmentResponse?> ResponseAsync(AppDbContext db, Guid id, CancellationToken token) =>
        (from appointment in db.Appointments.AsNoTracking()
         join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
         where appointment.Id == id
         select new AppointmentResponse(appointment.Id, appointment.Version, block.StaffMemberId, block.StaffName,
             block.ServiceDefinitionId, block.ServiceName, block.DurationMinutes, block.Price, block.Currency,
             block.StartsAt, block.EndsAt, block.Status, appointment.CustomerName, appointment.CustomerPhone,
             block.BufferBeforeMinutes, block.BufferAfterMinutes, block.OccupiedStartsAt, block.OccupiedEndsAt, block.PendingExpiresAt)).SingleOrDefaultAsync(token);

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
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = [$"Geçmiş bir gün veya {policy.HorizonDays} günlük rezervasyon ufkunun dışı seçilemez. Tarihi değiştirin."] });
        var availability = await AppointmentAvailability.FindAsync(db, member, service, schedule.IsConfigured, day, now, policy,
            service.DurationMinutes, service.BufferBeforeMinutes, service.BufferAfterMinutes, null, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new AvailabilityResponse("Europe/Istanbul", service.Version, availability.StartsAt, availability.UnavailableReasons));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, bool pending, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return AuthEndpoints.InvalidCsrf();
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
        // Confirmed isteklerinin önceki özetleri aynı kalır; diğer uç aynı anahtarla durum değiştiremez.
        if (pending) hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Pending:" + hash)));
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
        if (!AppointmentRules.InWindow(start, now, policy) || pending && start <= now) return Conflict("Başlangıç rezervasyon aralığına ve saat adımına uygun değil.");
        var end = start.AddMinutes(service.DurationMinutes);
        var occupiedStart = start.AddMinutes(-service.BufferBeforeMinutes);
        var occupiedEnd = end.AddMinutes(service.BufferAfterMinutes);
        var date = AppointmentCalendar.LocalDate(start);
        var business = await AppointmentCalendar.LoadAsync(db, null, date, date, timeout.Token);
        var staff = await AppointmentCalendar.LoadAsync(db, member.Id, date, date, timeout.Token);
        if (!business.Fits(occupiedStart, occupiedEnd, start) || !staff.Fits(occupiedStart, occupiedEnd, start))
            return Conflict("Randevu ve tampon sürelerinin tamamı etkin mesai içinde, mola ve izin dışında olmalı.");
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
            Status = pending ? "Pending" : "Confirmed",
            PendingExpiresAt = pending ? (now.AddMinutes(30) < start ? now.AddMinutes(30) : start) : null
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
        // PostgreSQL zaman hassasiyetini kullan; ilk yanıt ve tekrar aynı son anı döndürür.
        return Results.Created($"/api/appointments/{appointment.Id}", await ResponseAsync(db, appointment.Id, timeout.Token));
    }

    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);
}
