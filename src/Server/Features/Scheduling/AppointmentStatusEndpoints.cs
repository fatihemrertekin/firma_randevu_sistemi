using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class AppointmentStatusEndpoints
{
    public sealed record Request(Guid Version, Guid RequestKey);

    internal static void MapAppointmentStatusEndpoints(this RouteGroupBuilder appointments)
    {
        appointments.MapPost("/{id:guid}/confirm", (Guid id, Request request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => ChangeAsync(id, request, "Confirmed", context, csrf, db, users, clock));
        appointments.MapPost("/{id:guid}/cancel", (Guid id, Request request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => ChangeAsync(id, request, "Cancelled", context, csrf, db, users, clock));
        appointments.MapPost("/{id:guid}/complete", (Guid id, Request request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => ChangeAsync(id, request, "Completed", context, csrf, db, users, clock));
        appointments.MapPost("/{id:guid}/no-show", (Guid id, Request request, HttpContext context, IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) => ChangeAsync(id, request, "NoShow", context, csrf, db, users, clock));
    }

    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);

    private static async Task<IResult> ChangeAsync(Guid id, Request request, string status, HttpContext context,
        IAntiforgery csrf, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context); var token = timeout.Token;
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return AuthEndpoints.InvalidCsrf();
        if (id == Guid.Empty || request.Version == Guid.Empty || request.RequestKey == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Randevu, güncel sürüm ve işlem anahtarı gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, token, checkSubscription: false);
        if (owner is null) return Results.Unauthorized();
        var previous = await db.AppointmentStatusCommands.AsNoTracking().SingleOrDefaultAsync(item => item.ActorId == owner.Id && item.RequestKey == request.RequestKey, token);
        if (previous is not null)
            return previous.AppointmentId != id || previous.ExpectedVersion != request.Version || previous.Status != status
                ? Conflict("Aynı işlem anahtarı farklı işlem için kullanılamaz.")
                : Results.Ok(await AppointmentEndpoints.ResponseAsync(db, id, token));
        await Server.Features.Subscriptions.SubscriptionAccess.EnsureWritableAsync(context, db, token);
        var memberId = await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.AppointmentId == id).Select(item => (Guid?)item.StaffMemberId).SingleOrDefaultAsync(token);
        if (memberId is null) return Results.NotFound();
        // Rezervasyon ve takvim değişiklikleri de personelden önce randevu kilidi almaz.
        await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={memberId.Value} FOR UPDATE").SingleAsync(token);
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(token);
        var block = await db.AppointmentTimeBlocks.SingleAsync(item => item.AppointmentId == id, token);
        if (block.StaffMemberId != memberId || appointment.Version != request.Version) return Conflict("Randevu değişti. Güncel kaydı yükleyin.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (block.Status == "Pending")
        {
            if (block.PendingExpiresAt is not { } deadline || deadline <= now || now >= block.StartsAt) return Conflict("Bekleme süresi doldu; randevu onaylanamaz veya yeniden açılamaz.");
            if (status is not ("Confirmed" or "Cancelled")) return Conflict("Bekleyen randevu yalnız onaylanabilir veya iptal edilebilir.");
        }
        else if (block.Status != "Confirmed" || status == "Confirmed") return Conflict("Bu durumdaki randevu için bu işlem uygulanamaz.");
        if (block.Status == "Confirmed" && (status == "Cancelled" ? now >= block.StartsAt : now < block.StartsAt))
            return Conflict(status == "Cancelled" ? "Başlayan randevu sıradan iptalle açılmaz." : "Randevu başlamadan tamamlandı veya gelmedi olarak işaretlenemez.");
        block.Status = status; appointment.Version = Guid.NewGuid();
        await AppointmentReminders.SyncAsync(context, db, appointment, block, clock.GetUtcNow(), token);
        var audit = new AppointmentAudit { Id = Guid.NewGuid(), AppointmentId = id, ActorId = owner.Id, AppointmentVersion = appointment.Version, OccurredAt = now, Kind = status };
        db.AppointmentAudits.Add(audit);
        db.AppointmentStatusCommands.Add(new() { ActorId = owner.Id, RequestKey = request.RequestKey, AppointmentId = id, ExpectedVersion = request.Version, Status = status, AuditId = audit.Id });
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Results.Ok(await AppointmentEndpoints.ResponseAsync(db, id, token));
    }
}
