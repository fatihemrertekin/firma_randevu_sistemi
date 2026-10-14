using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Features.Business;
using Server.Features.Identity;
using Server.Features.Scheduling;
using Server.Infrastructure;

namespace Server.Features.Staff;

public static class StaffLeaveEndpoints
{
    private enum Operation { Create, Update, Remove }
    public sealed record CreateRequest(Guid Id, Guid Version, string? StartsAt, string? EndsAt);
    public sealed record UpdateRequest(Guid Version, string? StartsAt, string? EndsAt);
    public sealed record LeaveResponse(Guid Id, DateTime StartsAt, DateTime EndsAt, bool IsRemoved);
    public sealed record Response(Guid Version, string TimeZone, LeaveResponse Leave);
    public sealed record Page(Guid Version, string TimeZone, LeaveResponse[] Items, bool HasNextPage);
    internal static void MapStaffLeave(this RouteGroupBuilder members)
    {
        members.MapGet("/{id:guid}/leave", ReadAsync);
        members.MapPost("/{id:guid}/leave", (Guid id, CreateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => WriteAsync(id, request.Id, request.Version, request.StartsAt, request.EndsAt, Operation.Create, context, csrf, db, users, clock));
        members.MapPost("/{id:guid}/leave/{leaveId:guid}", (Guid id, Guid leaveId, UpdateRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => WriteAsync(id, leaveId, request.Version, request.StartsAt, request.EndsAt, Operation.Update, context, csrf, db, users, clock));
        members.MapPost("/{id:guid}/leave/{leaveId:guid}/remove", (Guid id, Guid leaveId, DateExceptionEndpoints.VersionRequest request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => WriteAsync(id, leaveId, request.Version, null, null, Operation.Remove, context, csrf, db, users, clock));
    }
    private static Response Result(Guid version, StaffLeave leave) => new(version, "Europe/Istanbul", new(leave.Id, leave.StartsAt, leave.EndsAt, leave.IsRemoved));
    private static bool Range(string? first, string? last, out DateTime start, out DateTime end)
    {
        var validStart = AppointmentRules.TryStart(first, out start); var validEnd = AppointmentRules.TryStart(last, out end);
        return validStart && validEnd && end > start && end - start <= TimeSpan.FromDays(366);
    }
    private static async Task<IResult> ReadAsync(Guid id, string from, string to, HttpContext context, AppDbContext db, int page = 1, int pageSize = 50)
    {
        using var timeout = DateExceptionEndpoints.Timeout(context);
        if (!Range(from, to, out var start, out var end) || page is < 1 or > 10000 || pageSize is < 1 or > 50) return Results.Problem(statusCode: 400, title: "Offsetli tam dakika aralığı en fazla 366 gün, sayfa boyutu 1–50 olmalı.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var version = await db.StaffMembers.Where(item => item.Id == id && !item.IsDeleted).Select(item => (Guid?)item.Version).SingleOrDefaultAsync(timeout.Token);
        if (version is null) return Results.NotFound();
        var items = await db.StaffLeaves.AsNoTracking().Where(item => item.StaffMemberId == id && !item.IsRemoved && item.StartsAt < end && item.EndsAt > start)
            .OrderBy(item => item.StartsAt).ThenBy(item => item.Id).Skip((page - 1) * pageSize).Take(pageSize + 1).Select(item => new LeaveResponse(item.Id, item.StartsAt, item.EndsAt, item.IsRemoved)).ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token); return Results.Ok(new Page(version.Value, "Europe/Istanbul", items.Take(pageSize).ToArray(), items.Length > pageSize));
    }
    private static async Task<IResult> WriteAsync(Guid id, Guid leaveId, Guid version, string? first, string? last, Operation operation,
        HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = DateExceptionEndpoints.Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        DateTime start = default, end = default;
        if (id == Guid.Empty || leaveId == Guid.Empty || version == Guid.Empty || (operation != Operation.Remove && !Range(first, last, out start, out end))) return Results.ValidationProblem(new Dictionary<string, string[]> { ["leave"] = ["Kimlik, güncel sürüm ve en fazla 366 günlük offsetli tam dakika aralığı gerekli."] });
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token); if (owner is null) return Results.Unauthorized();
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (member is null || member.IsDeleted) return Results.NotFound();
        if (!member.IsActive) return DateExceptionEndpoints.Conflict("Personel pasif; önce etkinleştirin.");
        var leave = await db.StaffLeaves.SingleOrDefaultAsync(item => item.Id == leaveId, timeout.Token);
        if (leave is not null && leave.StaffMemberId != id) return operation == Operation.Create ? DateExceptionEndpoints.Conflict("Bu izin kimliği daha önce kullanıldı.") : Results.NotFound();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{start:O}|{end:O}"))));
        if (operation == Operation.Create && leave is not null)
            return !leave.IsRemoved && leave.RequestHash == hash ? Results.Ok(Result(member.Version, leave)) : DateExceptionEndpoints.Conflict("Bu izin kimliği daha önce kullanıldı.");
        if (member.Version != version) return DateExceptionEndpoints.Conflict("Personel takvimi değişti. Güncel sürümü yükleyin.");
        if (operation != Operation.Create && leave is null) return Results.NotFound();
        if (leave is not null && ((operation == Operation.Remove && leave.IsRemoved) || (operation == Operation.Update && !leave.IsRemoved && leave.StartsAt == start && leave.EndsAt == end))) return Results.Ok(Result(member.Version, leave));
        if (operation == Operation.Update && leave is { IsRemoved: true }) return DateExceptionEndpoints.Conflict("Kaldırılmış izin düzenlenemez.");
        if (operation != Operation.Remove)
        {
            if (await db.StaffLeaves.AnyAsync(item => item.StaffMemberId == id && item.Id != leaveId && !item.IsRemoved && item.StartsAt < end && start < item.EndsAt, timeout.Token)) return DateExceptionEndpoints.Conflict("İzin başka bir izinle kesişiyor.");
            var now = clock.GetUtcNow().UtcDateTime;
            if (await db.AppointmentTimeBlocks.AnyAsync(item => item.StaffMemberId == id && (item.Status == "Pending" || item.Status == "Confirmed") && item.OccupiedEndsAt > now && item.OccupiedStartsAt < end && start < item.OccupiedEndsAt, timeout.Token)) return DateExceptionEndpoints.Conflict("İzin mevcut randevu veya tampon süresiyle kesişiyor.", true);
        }
        if (leave is null) { leave = new StaffLeave { Id = leaveId, StaffMemberId = id, RequestHash = hash }; db.Add(leave); }
        if (operation == Operation.Remove) leave.IsRemoved = true; else { leave.StartsAt = start; leave.EndsAt = end; }
        member.Version = Guid.NewGuid(); db.StaffHoursAudits.Add(new() { Id = Guid.NewGuid(), Kind = "Leave", ActorId = owner.Id, StaffMemberId = id, MemberVersion = member.Version, OccurredAt = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(timeout.Token); await transaction.CommitAsync(timeout.Token); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres &&
            ((postgres.SqlState == "23505" && postgres.ConstraintName == "PK_StaffLeaves") || (postgres.SqlState == "23P01" && postgres.ConstraintName == "EX_StaffLeaves_Time")))
        { return DateExceptionEndpoints.Conflict("İzin kimliği veya aralığı başka kayıtla çakışıyor."); }
        return Results.Ok(Result(member.Version, leave));
    }
}
