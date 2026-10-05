using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Business;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Staff;

public static class StaffHoursEndpoints
{
    public sealed record ScheduleResponse(StaffMemberEndpoints.MemberResponse Member, bool IsConfigured, string TimeZone, Guid Version, WeeklyHours.DayResponse[] Days);
    public sealed record UpdateRequest(Guid Version, WeeklyHours.DayRequest[]? Days);
    internal static void MapStaffHoursEndpoints(this RouteGroupBuilder members)
    {
        // Çağıran personel grubu MFA Owner ve staff-management limitini uygular.
        members.MapGet("/{id:guid}/hours", ReadAsync);
        members.MapPost("/{id:guid}/hours", UpdateAsync);
    }
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static ScheduleResponse Response(StaffMember member, StaffWorkingDay[] days) =>
        new(new(member.Id, member.Name, member.IsActive, member.Version), days.Length == 7, "Europe/Istanbul", member.Version,
            days.OrderBy(item => item.Day).Select(item => new WeeklyHours.DayResponse(item.Day, item.IsClosed, WeeklyHours.Hour(item.OpensAtMinute), WeeklyHours.Hour(item.ClosesAtMinute))).ToArray());
    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var member = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, timeout.Token);
        if (member is null || member.IsDeleted) return Results.NotFound();
        var days = await db.StaffWorkingDays.AsNoTracking().Where(item => item.StaffMemberId == id).ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(member, days));
    }
    private static async Task<IResult> UpdateAsync(Guid id, UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var desired = WeeklyHours.Validate(request.Version, request.Days, out var errors);
        if (errors.Count != 0) return Results.ValidationProblem(errors);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (member is null || member.IsDeleted) return Results.NotFound();
        if (member.Version != request.Version) return Results.Problem(statusCode: 409, title: "Personel kaydı veya saatleri değişti. Güncel saatleri yükleyin.");
        if (!member.IsActive) return Results.Problem(statusCode: 409, title: "Pasif personelin saatleri değiştirilemez. Önce personeli aktifleştirin.");
        var current = await db.StaffWorkingDays.Where(item => item.StaffMemberId == id).OrderBy(item => item.Day).ToArrayAsync(timeout.Token);
        if (current.Length == 7 && current.Zip(desired).All(pair => pair.First.Day == pair.Second.Day && pair.First.IsClosed == pair.Second.IsClosed && pair.First.OpensAtMinute == pair.Second.Start && pair.First.ClosesAtMinute == pair.Second.End))
            return Results.Ok(Response(member, current));
        foreach (var item in desired)
        {
            var day = current.SingleOrDefault(value => value.Day == item.Day);
            if (day is null) { day = new StaffWorkingDay { StaffMemberId = id, Day = item.Day }; db.StaffWorkingDays.Add(day); }
            day.IsClosed = item.IsClosed; day.OpensAtMinute = item.Start; day.ClosesAtMinute = item.End;
        }
        member.Version = Guid.NewGuid();
        db.StaffHoursAudits.Add(new StaffHoursAudit { Id = Guid.NewGuid(), StaffMemberId = id, ActorId = owner.Id, MemberVersion = member.Version, OccurredAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(member, desired.Select(item => new StaffWorkingDay { StaffMemberId = id, Day = item.Day, IsClosed = item.IsClosed, OpensAtMinute = item.Start, ClosesAtMinute = item.End }).ToArray()));
    }
}
