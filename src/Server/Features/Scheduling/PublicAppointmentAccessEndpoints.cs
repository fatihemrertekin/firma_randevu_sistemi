using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class PublicAppointmentAccessEndpoints
{
    public sealed record Issue(Guid ChallengeId, string? ChallengeSecret, bool ReplaceExisting);
    public sealed record Issued(string Token, Guid Version, DateTimeOffset ExpiresAt);
    public sealed record Cancel(Guid Version, Guid RequestKey);

    public static void MapPublicAppointmentAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/public-booking/appointments/{id:guid}/access", (Guid id, HttpContext ctx, IAntiforgery csrf,
            IConfiguration config, BookingVerificationSecurity security, AppDbContext db, TimeProvider clock) =>
            GuardAsync(ctx, () => BookingVerificationEndpoints.ExecuteAsync<Issue>(ctx, csrf,
                config.GetValue<bool>("PublicBooking:AccessEnabled") && security.Enabled,
                (body, token) => IssueAsync(id, body, db, security, clock, token))))
            .AllowAnonymous().RequireRateLimiting("booking-verification");
        app.MapGet("/api/public-booking/appointments/{id:guid}", (Guid id, HttpContext ctx, IConfiguration config,
            AppDbContext db, TimeProvider clock) => GuardAsync(ctx, () => ReadAsync(id, ctx, config, db, clock)))
            .AllowAnonymous().RequireRateLimiting("public-booking");
        app.MapPost("/api/public-booking/appointments/{id:guid}/cancel", (Guid id, HttpContext ctx, IAntiforgery csrf,
            IConfiguration config, AppDbContext db, TimeProvider clock) =>
            GuardAsync(ctx, () => BookingVerificationEndpoints.ExecuteAsync<Cancel>(ctx, csrf,
                config.GetValue<bool>("PublicBooking:AccessEnabled"),
                (body, token) => CancelAsync(id, body, ctx, db, clock, token))))
            .AllowAnonymous().RequireRateLimiting("booking-verification");
    }

    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Randevu erişim yetkisi geçersiz veya süresi dolmuş.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Randevu veya erişim değişti. Güncel sonucu yükleyin; iptal süresi dolduysa işletmeyle iletişime geçin.");

    private static async Task<IResult> GuardAsync(HttpContext ctx, Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (Exception exception) when (exception is PostgresException { SqlState: "55P03" } ||
            exception is InvalidOperationException { InnerException: PostgresException { SqlState: "55P03" } } ||
            exception is DbUpdateException { InnerException: PostgresException { SqlState: "55P03" } } ||
            exception is OperationCanceledException && !ctx.RequestAborted.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "İşlem şu anda tamamlanamıyor. Aynı isteği yeniden deneyin."); }
    }

    private static bool Receipt(BookingVerification? row, Guid id, Issue body, DateTimeOffset now) =>
        BookingVerificationEndpoints.Secret(row, body.ChallengeSecret) && row?.AppointmentId == id && row.ReplayUntil > now;
    private static string? TokenHash(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization;
        return header.Count == 1 && header[0] is { Length: 50 } value && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? BookingVerificationSecurity.SecretHash(value[7..]) : null;
    }
    private static bool Authorized(PublicAppointmentAccess? row, string? hash, DateTimeOffset now) =>
        row is not null && hash is not null && now < row.ExpiresAt && BookingVerificationSecurity.Equal(row.TokenHash, hash);

    private static async Task<IResult> IssueAsync(Guid id, Issue body, AppDbContext db, BookingVerificationSecurity security,
        TimeProvider clock, CancellationToken token)
    {
        var receipt = await db.BookingVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
        if (!Receipt(receipt, id, body, clock.GetUtcNow())) return Forbidden();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(token);
        if (appointment?.Source != "Public") return Forbidden();
        var access = await db.PublicAppointmentAccesses.FromSqlInterpolated($"SELECT * FROM \"PublicAppointmentAccesses\" WHERE \"AppointmentId\"={id} FOR UPDATE").SingleOrDefaultAsync(token);
        // Buradan sonra personel/politika kilidi alınmaz; create ve iptalin kilit sırası tersine dönmez.
        await security.LockAsync(db, token);
        receipt = await db.BookingVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
        var now = clock.GetUtcNow();
        if (!Receipt(receipt, id, body, now)) return Forbidden();
        if ((access is null) == body.ReplaceExisting) return Conflict();
        if (access is not null && now >= access.ExpiresAt) return Forbidden();
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = BookingVerificationSecurity.SecretHash(secret);
        if (access is null)
        {
            var start = await db.AppointmentTimeBlocks.Where(x => x.AppointmentId == id).Select(x => x.StartsAt).SingleAsync(token);
            var end = new DateTimeOffset(start).AddHours(24);
            if (end > now.AddDays(31)) end = now.AddDays(31);
            if (end <= now) return Forbidden();
            access = new() { AppointmentId = id, TokenHash = hash, FirstIssuedAt = now, ExpiresAt = end };
            db.PublicAppointmentAccesses.Add(access);
        }
        else access.TokenHash = hash;
        access.Version = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return Results.Json(new Issued(secret, access.Version, access.ExpiresAt), statusCode: body.ReplaceExisting ? 200 : 201);
    }

    private static async Task<IResult> ReadAsync(Guid id, HttpContext ctx, IConfiguration config, AppDbContext db, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(ctx);
        if (!config.GetValue<bool>("PublicBooking:AccessEnabled")) return Results.Problem(statusCode: 503, title: "Randevu erişimi şu anda kullanılamıyor.");
        var hash = TokenHash(ctx);
        if (hash is null) return Forbidden();
        var row = await (from access in db.PublicAppointmentAccesses.AsNoTracking()
                         where access.AppointmentId == id
                         from result in PublicAppointmentEndpoints.Query(db, id)
                         select new { Access = access, Result = result }).SingleOrDefaultAsync(timeout.Token);
        return row is not null && Authorized(row.Access, hash, clock.GetUtcNow()) ? Results.Ok(row.Result.Response()) : Forbidden();
    }

    private static async Task<IResult> CancelAsync(Guid id, Cancel body, HttpContext ctx, AppDbContext db, TimeProvider clock, CancellationToken token)
    {
        var hash = TokenHash(ctx);
        var access = await db.PublicAppointmentAccesses.AsNoTracking().SingleOrDefaultAsync(x => x.AppointmentId == id, token);
        if (!Authorized(access, hash, clock.GetUtcNow())) return Forbidden();
        if (body.Version == Guid.Empty || body.RequestKey == Guid.Empty) return Results.Problem(statusCode: 400, title: "Güncel randevu sürümü ve işlem anahtarı gerekli.");
        var memberId = await db.AppointmentTimeBlocks.AsNoTracking().Where(x => x.AppointmentId == id).Select(x => x.StaffMemberId).SingleAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
        await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={memberId} FOR UPDATE").SingleAsync(token);
        var appointment = await db.Appointments.FromSqlInterpolated($"SELECT * FROM \"Appointments\" WHERE \"Id\"={id} FOR UPDATE").SingleAsync(token);
        access = await db.PublicAppointmentAccesses.FromSqlInterpolated($"SELECT * FROM \"PublicAppointmentAccesses\" WHERE \"AppointmentId\"={id} FOR UPDATE").SingleOrDefaultAsync(token);
        var now = clock.GetUtcNow();
        if (appointment.Source != "Public" || !Authorized(access, hash, now) || access is null) return Forbidden();
        if (access.CancellationRequestKey is { } previous)
            return previous == body.RequestKey && access.CancellationExpectedVersion == body.Version
                ? Results.Ok(await PublicAppointmentEndpoints.ReadAsync(db, id, token)) : Conflict();
        var block = await db.AppointmentTimeBlocks.SingleAsync(x => x.AppointmentId == id, token);
        if (block.StaffMemberId != memberId || appointment.Version != body.Version ||
            block.Status is not ("Pending" or "Confirmed") || now.UtcDateTime >= block.StartsAt ||
            appointment.CustomerCancellationLeadMinutes is not { } minutes || now.UtcDateTime > block.StartsAt.AddMinutes(-minutes) ||
            block.Status == "Pending" && (block.PendingExpiresAt is null || now.UtcDateTime >= block.PendingExpiresAt)) return Conflict();
        block.Status = "Cancelled";
        appointment.Version = Guid.NewGuid();
        await AppointmentReminders.SyncAsync(ctx, db, appointment, block, now, token);
        var audit = new AppointmentAudit { Id = Guid.NewGuid(), AppointmentId = id, AppointmentVersion = appointment.Version, OccurredAt = now.UtcDateTime, Kind = "PublicCancelled" };
        db.AppointmentAudits.Add(audit);
        access.CancellationRequestKey = body.RequestKey; access.CancellationExpectedVersion = body.Version; access.CancellationAuditId = audit.Id;
        access.Version = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        var response = await PublicAppointmentEndpoints.ReadAsync(db, id, token);
        await transaction.CommitAsync(token);
        return Results.Ok(response);
    }
}
