using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Features.Scheduling;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class WeeklyBreakEndpoints
{
    internal static void MapBusinessBreaks(this RouteGroupBuilder hours)
    {
        hours.MapGet("/breaks", (HttpContext context, AppDbContext db) => ReadAsync(null, context, db));
        hours.MapPost("/breaks", (WeeklyBreaks.UpdateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(null, request, context, csrf, db, users, clock));
    }
    internal static void MapStaffBreaks(this RouteGroupBuilder members)
    {
        members.MapGet("/{id:guid}/breaks", (Guid id, HttpContext context, AppDbContext db) => ReadAsync(id, context, db));
        members.MapPost("/{id:guid}/breaks", (Guid id, WeeklyBreaks.UpdateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => UpdateAsync(id, request, context, csrf, db, users, clock));
    }
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static async Task<IResult> ReadAsync(Guid? id, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var member = id is null ? null : await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, timeout.Token);
        if (id is not null && member is null) return Results.NotFound();
        var hours = id is null ? await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(timeout.Token) : null;
        var version = hours?.Version ?? member?.Version;
        if (version is null) return Results.NotFound();
        var configured = hours?.IsConfigured ?? await db.StaffWorkingDays.CountAsync(item => item.StaffMemberId == id, timeout.Token) == 7;
        var values = await WeeklyBreaks.LoadAsync(db, id, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(WeeklyBreaks.Result(configured, version.Value, values));
    }
    private static async Task<IResult> UpdateAsync(Guid? id, WeeklyBreaks.UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return AuthEndpoints.InvalidCsrf();
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var hours = id is null ? await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\" = 1 FOR UPDATE").SingleAsync(timeout.Token) : null;
        var member = id is null ? null : await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {id.Value} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (id is not null && (member is null || member.IsDeleted)) return Results.NotFound();
        var week = id is null
            ? await db.BusinessOpeningDays.AsNoTracking().Select(day => new WeeklyHours.DayValue(day.Day, day.IsClosed, day.OpensAtMinute, day.ClosesAtMinute)).ToArrayAsync(timeout.Token)
            : await db.StaffWorkingDays.AsNoTracking().Where(day => day.StaffMemberId == id).Select(day => new WeeklyHours.DayValue(day.Day, day.IsClosed, day.OpensAtMinute, day.ClosesAtMinute)).ToArrayAsync(timeout.Token);
        var values = WeeklyBreaks.Validate(request.Version, request.Days, week, out var errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if ((hours is not null && !hours.IsConfigured) || week.Length != 7 || member is { IsActive: false })
            return Results.Problem(statusCode: 409, title: "Önce aktif personel ve haftalık çalışma saatlerini yapılandırın.");
        var version = hours?.Version ?? member?.Version;
        if (version is null) return Results.NotFound();
        if (version != request.Version) return Results.Problem(statusCode: 409, title: "Çalışma saatleri veya molalar değişti. Güncel kaydı yükleyin.");
        var current = await WeeklyBreaks.LoadAsync(db, id, timeout.Token);
        if (current.SequenceEqual(values)) return Results.Ok(WeeklyBreaks.Result(true, version.Value, current));
        if (!await AppointmentRules.FitsWeekAsync(db, week, clock.GetUtcNow().UtcDateTime, id, timeout.Token, values))
            return Results.Problem(statusCode: 409, title: "Yeni molalar mevcut randevu veya tampon süresiyle kesişiyor.", extensions: new Dictionary<string, object?> { ["code"] = "appointments_outside_hours" });
        var changed = Guid.NewGuid();
        // Tam haftanın değişimi tek transaction'dadır; aradaki boş liste başka işleme görünmez.
        if (hours is not null)
        {
            await db.BusinessWeeklyBreaks.ExecuteDeleteAsync(timeout.Token);
            db.BusinessWeeklyBreaks.AddRange(values.Select(item => new BusinessWeeklyBreak { ScheduleId = 1, Day = item.Day, Position = item.Position, StartsAtMinute = item.Start, EndsAtMinute = item.End }));
            hours.Version = changed;
            db.BusinessHoursAudits.Add(new BusinessHoursAudit { Id = Guid.NewGuid(), Kind = "Breaks", ActorId = owner.Id, ScheduleVersion = changed, OccurredAt = clock.GetUtcNow() });
        }
        else if (member is not null)
        {
            await db.StaffWeeklyBreaks.Where(item => item.StaffMemberId == id).ExecuteDeleteAsync(timeout.Token);
            db.StaffWeeklyBreaks.AddRange(values.Select(item => new StaffWeeklyBreak { StaffMemberId = member.Id, Day = item.Day, Position = item.Position, StartsAtMinute = item.Start, EndsAtMinute = item.End }));
            member.Version = changed;
            db.StaffHoursAudits.Add(new StaffHoursAudit { Id = Guid.NewGuid(), Kind = "Breaks", StaffMemberId = member.Id, ActorId = owner.Id, MemberVersion = changed, OccurredAt = clock.GetUtcNow() });
        }
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(WeeklyBreaks.Result(true, changed, values));
    }
}
