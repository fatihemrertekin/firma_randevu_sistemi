using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Features.Scheduling;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class DateExceptionEndpoints
{
    public sealed record UpdateRequest(Guid Version, bool? IsClosed, string? OpensAt, string? ClosesAt, WeeklyBreaks.BreakRequest[]? Breaks);
    public sealed record VersionRequest(Guid Version);
    public sealed record Response(Guid Version, DateOnly Date, string TimeZone, bool IsClosed, string? OpensAt, string? ClosesAt, WeeklyBreaks.BreakResponse[] Breaks);
    internal static void MapBusinessExceptions(this RouteGroupBuilder hours)
    {
        hours.MapGet("/exceptions/{date}", (string date, HttpContext context, AppDbContext db) => ReadAsync(null, date, context, db));
        hours.MapPost("/exceptions/{date}", (string date, UpdateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(null, date, request, false, context, csrf, db, users, clock));
        hours.MapPost("/exceptions/{date}/remove", (string date, VersionRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(null, date, new(request.Version, null, null, null, null), true, context, csrf, db, users, clock));
    }
    internal static void MapStaffExceptions(this RouteGroupBuilder members)
    {
        members.MapGet("/{id:guid}/exceptions/{date}", (Guid id, string date, HttpContext context, AppDbContext db) => ReadAsync(id, date, context, db));
        members.MapPost("/{id:guid}/exceptions/{date}", (Guid id, string date, UpdateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(id, date, request, false, context, csrf, db, users, clock));
        members.MapPost("/{id:guid}/exceptions/{date}/remove", (Guid id, string date, VersionRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(id, date, new(request.Version, null, null, null, null), true, context, csrf, db, users, clock));
    }
    internal static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store"; var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(15)); return timeout;
    }
    internal static bool TryDate(string text, out DateOnly date) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    internal static IResult Conflict(string title, bool appointments = false) => Results.Problem(statusCode: 409, title: title,
        extensions: appointments ? new Dictionary<string, object?> { ["code"] = "appointments_outside_hours" } : null);
    private static Response Result(Guid version, DateOnly date, AppointmentCalendar.Day day) => new(version, date, "Europe/Istanbul", day.IsClosed, WeeklyHours.Hour(day.Start), WeeklyHours.Hour(day.End),
        day.Breaks.Select(item => new WeeklyBreaks.BreakResponse(WeeklyHours.Hour(item.Start)!, WeeklyHours.Hour(item.End)!)).ToArray());
    private static async Task<AppointmentCalendar.Day?> DayAsync(AppDbContext db, Guid? id, DateOnly date, CancellationToken token)
    {
        var weekday = AppointmentCalendar.Weekday(date);
        var header = id is null
            ? await db.BusinessDateExceptions.AsNoTracking().Where(item => item.Date == date).Select(item => new WeeklyHours.DayValue(weekday, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).SingleOrDefaultAsync(token)
            : await db.StaffDateExceptions.AsNoTracking().Where(item => item.StaffMemberId == id && item.Date == date).Select(item => new WeeklyHours.DayValue(weekday, item.IsClosed, item.OpensAtMinute, item.ClosesAtMinute)).SingleOrDefaultAsync(token);
        if (header is null) return null;
        var breaks = id is null
            ? await db.BusinessDateBreaks.AsNoTracking().Where(item => item.Date == date).OrderBy(item => item.Position).Select(item => new WeeklyBreaks.Value(weekday, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token)
            : await db.StaffDateBreaks.AsNoTracking().Where(item => item.StaffMemberId == id && item.Date == date).OrderBy(item => item.Position).Select(item => new WeeklyBreaks.Value(weekday, item.Position, item.StartsAtMinute, item.EndsAtMinute)).ToArrayAsync(token);
        return new(header.IsClosed, header.Start, header.End, breaks);
    }
    private static async Task<IResult> ReadAsync(Guid? id, string input, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context); if (!TryDate(input, out var date)) return Results.Problem(statusCode: 400, title: "Tarih yyyy-MM-dd biçiminde olmalı.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var version = id is null ? await db.BusinessHoursSchedules.Select(item => (Guid?)item.Version).SingleAsync(timeout.Token)
            : await db.StaffMembers.Where(item => item.Id == id && !item.IsDeleted).Select(item => (Guid?)item.Version).SingleOrDefaultAsync(timeout.Token);
        if (version is null) return Results.NotFound();
        var day = await DayAsync(db, id, date, timeout.Token); await transaction.CommitAsync(timeout.Token);
        return day is null ? Results.NotFound() : Results.Ok(Result(version.Value, date, day));
    }
    private static async Task<IResult> UpdateAsync(Guid? id, string input, UpdateRequest request, bool remove, HttpContext context,
        IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return AuthEndpoints.InvalidCsrf();
        var errors = new Dictionary<string, string[]>();
        if (!TryDate(input, out var date)) errors["date"] = ["Tarih yyyy-MM-dd biçiminde olmalı."];
        if (request.Version == Guid.Empty) errors["version"] = ["Güncel parent sürümü gerekli."];
        var start = WeeklyHours.Minute(request.OpensAt); var end = WeeklyHours.Minute(request.ClosesAt);
        WeeklyBreaks.Value[] breaks = [];
        if (!remove)
        {
            if (request.IsClosed is null || (request.IsClosed == true && (request.OpensAt is not null || request.ClosesAt is not null))) errors["isClosed"] = ["Gün durumu gerekli; kapalı günde saat gönderilmez."];
            if (request.IsClosed == false && (start is null || end is null || start >= end)) errors["hours"] = ["Aynı gün içinde artan SS:dd saatleri gerekli."];
            breaks = WeeklyBreaks.ValidateDay(AppointmentCalendar.Weekday(date), request.Breaks, new(AppointmentCalendar.Weekday(date), request.IsClosed == true, start, end), out var error);
            if (error is not null) errors["breaks"] = [error];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token); if (owner is null) return Results.Unauthorized();
        var schedule = id is null ? await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR UPDATE").SingleAsync(timeout.Token) : null;
        var member = id is null ? null : await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={id.Value} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (id is not null && (member is null || member.IsDeleted)) return Results.NotFound();
        if (schedule is { IsConfigured: false } || member is { IsActive: false } || (id is not null && await db.StaffWorkingDays.CountAsync(item => item.StaffMemberId == id, timeout.Token) != 7)) return Conflict("Önce haftalık saatleri ve aktif personeli yapılandırın.");
        var version = schedule?.Version ?? member?.Version ?? Guid.Empty; if (version != request.Version) return Conflict("Takvim değişti. Güncel sürümü yükleyin.");
        var current = await DayAsync(db, id, date, timeout.Token);
        var proposed = remove ? null : new AppointmentCalendar.Day(request.IsClosed == true, start, end, breaks);
        var same = current is null ? proposed is null : proposed is not null && current.IsClosed == proposed.IsClosed && current.Start == proposed.Start && current.End == proposed.End && current.Breaks.SequenceEqual(proposed.Breaks);
        if (same) return proposed is null ? Results.Ok(new { version, date, removed = true }) : Results.Ok(Result(version, date, proposed));
        if (!await AppointmentCalendar.FitsAppointmentsAsync(db, id, clock.GetUtcNow().UtcDateTime, timeout.Token, date: date, proposed: proposed)) return Conflict("Tarihli gün değişikliği mevcut randevu veya tampon süresini dışarıda bırakıyor.", true);
        if (id is null)
        {
            await db.BusinessDateBreaks.Where(item => item.Date == date).ExecuteDeleteAsync(timeout.Token);
            var header = await db.BusinessDateExceptions.SingleOrDefaultAsync(item => item.Date == date, timeout.Token);
            if (remove) { if (header is not null) db.Remove(header); }
            else
            {
                if (header is null) { header = new BusinessDateException { ScheduleId = 1, Date = date }; db.Add(header); }
                header.IsClosed = request.IsClosed == true; header.OpensAtMinute = start; header.ClosesAtMinute = end;
                db.BusinessDateBreaks.AddRange(breaks.Select(item => new BusinessDateBreak { ScheduleId = 1, Date = date, Position = item.Position, StartsAtMinute = item.Start, EndsAtMinute = item.End }));
            }
            if (schedule is not null) { schedule.Version = Guid.NewGuid(); version = schedule.Version; db.BusinessHoursAudits.Add(new() { Id = Guid.NewGuid(), Kind = "Exception", ActorId = owner.Id, ScheduleVersion = version, OccurredAt = clock.GetUtcNow() }); }
        }
        else if (member is not null)
        {
            await db.StaffDateBreaks.Where(item => item.StaffMemberId == id && item.Date == date).ExecuteDeleteAsync(timeout.Token);
            var header = await db.StaffDateExceptions.SingleOrDefaultAsync(item => item.StaffMemberId == id && item.Date == date, timeout.Token);
            if (remove) { if (header is not null) db.Remove(header); }
            else
            {
                if (header is null) { header = new StaffDateException { StaffMemberId = member.Id, Date = date }; db.Add(header); }
                header.IsClosed = request.IsClosed == true; header.OpensAtMinute = start; header.ClosesAtMinute = end;
                db.StaffDateBreaks.AddRange(breaks.Select(item => new StaffDateBreak { StaffMemberId = member.Id, Date = date, Position = item.Position, StartsAtMinute = item.Start, EndsAtMinute = item.End }));
            }
            member.Version = Guid.NewGuid(); version = member.Version; db.StaffHoursAudits.Add(new() { Id = Guid.NewGuid(), Kind = "Exception", ActorId = owner.Id, StaffMemberId = member.Id, MemberVersion = member.Version, OccurredAt = clock.GetUtcNow() });
        }
        await db.SaveChangesAsync(timeout.Token); await transaction.CommitAsync(timeout.Token);
        return proposed is null ? Results.Ok(new { version, date, removed = true }) : Results.Ok(Result(version, date, proposed));
    }
}
