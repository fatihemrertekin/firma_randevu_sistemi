using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class BusinessHoursEndpoints
{
    public sealed record ScheduleResponse(bool IsConfigured, string TimeZone, Guid Version, WeeklyHours.DayResponse[] Days);
    public sealed record UpdateRequest(Guid Version, WeeklyHours.DayRequest[]? Days);

    public static void MapBusinessHoursEndpoints(this IEndpointRouteBuilder app)
    {
        var hours = app.MapGroup("/api/business-hours").RequireAuthorization("Owner").RequireRateLimiting("business-hours");
        hours.MapGet("/", ReadAsync);
        hours.MapPost("/", UpdateAsync);
    }

    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static ScheduleResponse Response(BusinessHoursSchedule schedule, BusinessOpeningDay[] days) =>
        new(schedule.IsConfigured, "Europe/Istanbul", schedule.Version,
            days.OrderBy(item => item.Day).Select(item => new WeeklyHours.DayResponse(item.Day, item.IsClosed, WeeklyHours.Hour(item.OpensAtMinute), WeeklyHours.Hour(item.ClosesAtMinute))).ToArray());
    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(timeout.Token);
        var days = await db.BusinessOpeningDays.AsNoTracking().ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(schedule, days));
    }

    private static async Task<IResult> UpdateAsync(UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var desired = WeeklyHours.Validate(request.Version, request.Days, out var errors).Select(item => new BusinessOpeningDay { ScheduleId = 1, Day = item.Day, IsClosed = item.IsClosed, OpensAtMinute = item.Start, ClosesAtMinute = item.End }).ToArray();
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var schedule = await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\" = 1 FOR UPDATE").SingleAsync(timeout.Token);
        if (schedule.Version != request.Version) return Results.Problem(statusCode: 409, title: "İşletme saatleri başka bir işlemde değişti. Güncel saatleri yükleyin.");
        var current = await db.BusinessOpeningDays.OrderBy(item => item.Day).ToArrayAsync(timeout.Token);
        if (schedule.IsConfigured && current.Length == 7 && current.Zip(desired).All(pair =>
            pair.First.Day == pair.Second.Day && pair.First.IsClosed == pair.Second.IsClosed && pair.First.OpensAtMinute == pair.Second.OpensAtMinute && pair.First.ClosesAtMinute == pair.Second.ClosesAtMinute))
            return Results.Ok(Response(schedule, current));
        foreach (var item in desired)
        {
            var existing = current.SingleOrDefault(day => day.Day == item.Day);
            if (existing is null) db.BusinessOpeningDays.Add(item);
            else { existing.IsClosed = item.IsClosed; existing.OpensAtMinute = item.OpensAtMinute; existing.ClosesAtMinute = item.ClosesAtMinute; }
        }
        schedule.IsConfigured = true; schedule.Version = Guid.NewGuid();
        db.BusinessHoursAudits.Add(new BusinessHoursAudit { Id = Guid.NewGuid(), ActorId = owner.Id, ScheduleVersion = schedule.Version, OccurredAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(schedule, desired));
    }
}
