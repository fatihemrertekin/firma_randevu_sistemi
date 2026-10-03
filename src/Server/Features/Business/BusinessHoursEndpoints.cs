using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class BusinessHoursEndpoints
{
    public sealed record DayRequest(int Day, bool? IsClosed, string? OpensAt, string? ClosesAt);
    public sealed record DayResponse(int Day, bool IsClosed, string? OpensAt, string? ClosesAt);
    public sealed record ScheduleResponse(bool IsConfigured, string TimeZone, Guid Version, DayResponse[] Days);
    public sealed record UpdateRequest(Guid Version, DayRequest[]? Days);

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
    private static string? Hour(int? minute) => minute is null ? null : new TimeOnly(minute.Value / 60, minute.Value % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
    private static ScheduleResponse Response(BusinessHoursSchedule schedule, BusinessOpeningDay[] days) =>
        new(schedule.IsConfigured, "Europe/Istanbul", schedule.Version,
            days.OrderBy(item => item.Day).Select(item => new DayResponse(item.Day, item.IsClosed, Hour(item.OpensAtMinute), Hour(item.ClosesAtMinute))).ToArray());
    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(timeout.Token);
        var days = await db.BusinessOpeningDays.AsNoTracking().ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(schedule, days));
    }

    private static int? Minute(string? text) => text is { Length: 5 } &&
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value.Hour * 60 + value.Minute : null;
    private static BusinessOpeningDay[] Validate(UpdateRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];
        if (request.Version == Guid.Empty) errors["days"] = ["Saatlerin güncel sürümü gerekli. Güncel saatleri yükleyin."];
        if (request.Days is not { Length: 7 } || request.Days.Any(item => item is null || item.Day is < 0 or > 6) || request.Days.Select(item => item.Day).Distinct().Count() != 7)
        {
            errors["days"] = ["Haftanın yedi günü birer kez gönderilmeli."];
            return [];
        }
        var days = new List<BusinessOpeningDay>();
        foreach (var item in request.Days)
        {
            var start = Minute(item.OpensAt); var end = Minute(item.ClosesAt);
            if (item.IsClosed is null || (item.IsClosed == true && (item.OpensAt is not null || item.ClosesAt is not null)))
                errors[$"day{item.Day}Closed"] = ["Açık/kapalı seçimi gerekli; kapalı günde saat gönderilmez."];
            if (item.IsClosed == false)
            {
                if (start is null) errors[$"day{item.Day}OpensAt"] = ["Açılışı SS:dd biçiminde girin (00:00–23:59)."];
                if (end is null) errors[$"day{item.Day}ClosesAt"] = ["Kapanışı SS:dd biçiminde girin (00:00–23:59)."];
                else if (start is not null && end <= start) errors[$"day{item.Day}ClosesAt"] = ["Kapanış aynı gün içinde açılıştan sonra olmalı."];
            }
            days.Add(new BusinessOpeningDay { ScheduleId = 1, Day = item.Day, IsClosed = item.IsClosed == true, OpensAtMinute = start, ClosesAtMinute = end });
        }
        return days.OrderBy(item => item.Day).ToArray();
    }
    private static async Task<IResult> UpdateAsync(UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var desired = Validate(request, out var errors);
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
