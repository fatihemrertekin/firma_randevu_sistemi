using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class PublicBookingEndpoints
{
    public sealed record BusinessResponse(string Name, string? Phone, string? Address);
    public sealed record PolicyResponse(Guid Version, int LeadMinutes, int HorizonDays, int SlotMinutes, string TimeZone);
    public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, string Price, string Currency, Guid Version);
    public sealed record CatalogResponse(BusinessResponse Business, PolicyResponse Policy, ServiceResponse[] Items,
        int Page, bool HasMore, int PageSize, int TotalCount);
    public sealed record StaffResponse(Guid Id, string Name);
    public sealed record StaffPage(StaffResponse[] Items, int Page, bool HasMore, int PageSize, int TotalCount);
    public sealed record AvailabilityResponse(string TimeZone, Guid ServiceVersion, Guid PolicyVersion, DateTime[] StartsAt);

    public static void MapPublicBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var booking = app.MapGroup("/api/public-booking").AllowAnonymous().RequireRateLimiting("public-booking");
        booking.MapGet("/catalog", CatalogAsync);
        booking.MapGet("/staff", StaffAsync);
        booking.MapGet("/availability", AvailabilityAsync);
    }

    private static bool ValidPage(int page, int pageSize) => page is >= 1 and <= 10000 && pageSize is >= 1 and <= 50;

    private static async Task<IResult> CatalogAsync(HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        if (!ValidPage(page, pageSize))
            return Results.Problem(statusCode: 400, title: "Geçerli sayfa ve 1–50 arası sayfa boyutu gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var business = await db.BusinessProfiles.AsNoTracking()
            .Select(item => new BusinessResponse(item.Name, item.Phone, item.Address)).SingleAsync(timeout.Token);
        var policy = await db.BookingPolicies.AsNoTracking().Select(item => new PolicyResponse(
            item.Version, item.LeadMinutes, item.HorizonDays, item.SlotMinutes, "Europe/Istanbul")).SingleAsync(timeout.Token);
        var query = db.ServiceDefinitions.AsNoTracking().Where(item => item.IsActive && !item.IsDeleted);
        var totalCount = await query.CountAsync(timeout.Token);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize)));
        var rows = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(item => new { item.Id, item.Name, item.DurationMinutes, item.Price, item.Currency, item.Version }).ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        var items = rows.Take(pageSize).Select(item => new ServiceResponse(item.Id, item.Name, item.DurationMinutes,
            item.Price.ToString("0.00", CultureInfo.InvariantCulture), item.Currency, item.Version)).ToArray();
        return Results.Ok(new CatalogResponse(business, policy, items, page, rows.Length > pageSize, pageSize, totalCount));
    }

    private static async Task<IResult> StaffAsync(Guid serviceId, HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        if (serviceId == Guid.Empty || !ValidPage(page, pageSize))
            return Results.Problem(statusCode: 400, title: "Geçerli hizmet, sayfa ve 1–50 arası sayfa boyutu gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        if (!await db.ServiceDefinitions.AnyAsync(item => item.Id == serviceId && item.IsActive && !item.IsDeleted, timeout.Token))
            return Results.NotFound();
        var query = db.StaffMembers.AsNoTracking().Where(item => item.IsActive && !item.IsDeleted &&
            db.StaffServiceAssignments.Any(assignment => assignment.StaffMemberId == item.Id && assignment.ServiceDefinitionId == serviceId));
        var totalCount = await query.CountAsync(timeout.Token);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling((double)totalCount / pageSize)));
        var rows = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1).Select(item => new StaffResponse(item.Id, item.Name)).ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new StaffPage(rows.Take(pageSize).ToArray(), page, rows.Length > pageSize, pageSize, totalCount));
    }

    private static async Task<IResult> AvailabilityAsync(Guid serviceId, Guid staffMemberId, string date,
        HttpContext context, AppDbContext db, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        if (serviceId == Guid.Empty || staffMemberId == Guid.Empty ||
            !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Results.Problem(statusCode: 400, title: "Geçerli hizmet, çalışan ve tarih gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var member = await db.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == staffMemberId && item.IsActive && !item.IsDeleted, timeout.Token);
        var service = await db.ServiceDefinitions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == serviceId && item.IsActive && !item.IsDeleted, timeout.Token);
        if (member is null || service is null || !await db.StaffServiceAssignments.AnyAsync(
            item => item.StaffMemberId == member.Id && item.ServiceDefinitionId == service.Id, timeout.Token))
            return Results.NotFound();
        var policy = await db.BookingPolicies.AsNoTracking().SingleAsync(timeout.Token);
        var now = clock.GetUtcNow().UtcDateTime;
        var localNow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, AppointmentRules.Istanbul));
        if (day < localNow || day > localNow.AddDays(policy.HorizonDays))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["date"] = [$"Bugünden başlayarak {policy.HorizonDays} günlük aralıkta bir tarih seçin."] });
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(timeout.Token);
        var availability = await AppointmentAvailability.FindAsync(db, member, service, schedule.IsConfigured, day, now, policy,
            service.DurationMinutes, service.BufferBeforeMinutes, service.BufferAfterMinutes, null, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        // Yönetim gerekçeleri izin/mola ve işletim ayrıntısı içerir; kamu yalnız boş saatleri görür.
        return Results.Ok(new AvailabilityResponse("Europe/Istanbul", service.Version, policy.Version, availability.StartsAt));
    }
}
