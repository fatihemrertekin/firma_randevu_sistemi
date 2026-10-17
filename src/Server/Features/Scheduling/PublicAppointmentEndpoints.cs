using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class PublicAppointmentEndpoints
{
    public sealed record Request(Guid RequestKey, Guid ChallengeId, string? ChallengeSecret, Guid ServiceId, Guid ServiceVersion,
        Guid PolicyVersion, Guid StaffMemberId, string? StartsAt, string? CustomerName, string? CustomerPhone);
    public sealed record Response(Guid Id, Guid Version, string ServiceName, string StaffName, DateTime StartsAt, DateTime EndsAt,
        string Price, string Currency, string Status, DateTime? PendingExpiresAt, Guid PolicyVersion,
        int CustomerCancellationLeadMinutes, bool RequiresBusinessApproval, DateTime CancellationDeadline);

    public static void MapPublicAppointmentEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/public-booking/appointments", (HttpContext ctx, IAntiforgery csrf, BookingVerificationSecurity security,
            AppDbContext db, TimeProvider clock) => BookingVerificationEndpoints.ExecuteAsync<Request>(ctx, csrf, security.Enabled,
                (body, token) => CreateAsync(body, ctx, db, security, clock, token)))
        .AllowAnonymous().RequireRateLimiting("booking-verification");

    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Doğrulama yetkisi geçersiz.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Seçim değişti veya işlem anahtarı daha önce kullanıldı.");

    private static IResult? Authorize(BookingVerification? item, Request body, string hash, DateTimeOffset now)
    {
        if (!BookingVerificationEndpoints.Secret(item, body.ChallengeSecret) || item is null ||
            (item.AppointmentId is not null ? item.ReplayUntil <= now : item.VerifiedUntil is null || item.VerifiedUntil <= now)) return Forbidden();
        return item.RequestKey != body.RequestKey || !BookingVerificationSecurity.Equal(item.RequestHash, hash) ? Conflict() : null;
    }

    internal sealed record Snapshot(Guid Id, Guid Version, string ServiceName, string StaffName, DateTime StartsAt, DateTime EndsAt,
        decimal Price, string Currency, string Status, DateTime? PendingExpiresAt, Guid? PolicyVersion,
        int? CustomerCancellationLeadMinutes, bool? RequiresBusinessApproval)
    {
        internal Response Response()
        {
            if (PolicyVersion is not { } policy || CustomerCancellationLeadMinutes is not { } minutes ||
                RequiresBusinessApproval is not { } approval) throw new InvalidOperationException("Müşteri politika snapshot'ı eksik.");
            return new(Id, Version, ServiceName, StaffName, StartsAt, EndsAt, Price.ToString("F2", CultureInfo.InvariantCulture),
                Currency, Status, PendingExpiresAt, policy, minutes, approval, StartsAt.AddMinutes(-minutes));
        }
    }

    internal static IQueryable<Snapshot> Query(AppDbContext db, Guid id) =>
        from appointment in db.Appointments.AsNoTracking()
        join block in db.AppointmentTimeBlocks.AsNoTracking() on appointment.Id equals block.AppointmentId
        where appointment.Id == id && appointment.Source == "Public"
        select new Snapshot(appointment.Id, appointment.Version, block.ServiceName, block.StaffName,
            block.StartsAt, block.EndsAt, block.Price, block.Currency, block.Status, block.PendingExpiresAt,
            appointment.BookingPolicyVersion, appointment.CustomerCancellationLeadMinutes, appointment.RequiresBusinessApproval);

    internal static async Task<Response> ReadAsync(AppDbContext db, Guid id, CancellationToken token) =>
        (await Query(db, id).SingleAsync(token)).Response();

    private static async Task<IResult> CreateAsync(Request body, HttpContext ctx, AppDbContext db,
        BookingVerificationSecurity security, TimeProvider clock, CancellationToken token)
    {
        var intent = new BookingVerificationEndpoints.Request(body.RequestKey, body.ServiceId, body.ServiceVersion,
            body.PolicyVersion, body.StaffMemberId, body.StartsAt, body.CustomerName, body.CustomerPhone);
        if (!BookingVerificationEndpoints.TryNormalize(intent, out var name, out var phone, out var start))
            return Results.Problem(statusCode: 400, title: "Geçerli randevu bilgileri gerekli.");
        var hash = BookingVerificationEndpoints.RequestHash(intent, name, phone, start, security);
        var item = await db.BookingVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
        var error = Authorize(item, body, hash, clock.GetUtcNow());
        if (error is not null) return error;
        if (item?.AppointmentId is { } previous) return Results.Ok(await ReadAsync(db, previous, token));
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            var creation = await AppointmentCreation.LockAsync(db, body.StaffMemberId, body.ServiceId, token);
            // OTP kilidinden sonra takvim/personel kilidi alınmaz; worker aynı sırayı tersine çeviremez.
            await security.LockAsync(db, token);
            item = await db.BookingVerifications.SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
            var now = clock.GetUtcNow();
            error = Authorize(item, body, hash, now);
            if (error is not null || item is null) return error ?? Forbidden();
            if (item.AppointmentId is { } raced) return Results.Ok(await ReadAsync(db, raced, token));
            if (await db.Appointments.AnyAsync(x => x.Source == "Public" && x.RequestKey == body.RequestKey, token)) return Conflict();
            if (creation.Policy.Version != body.PolicyVersion) return Conflict();
            var (block, invalid) = await creation.PrepareAsync(db, body.ServiceVersion, start, now.UtcDateTime,
                creation.Policy.RequiresBusinessApproval, token);
            if (invalid is not null) return invalid;
            if (block is null) throw new InvalidOperationException("Randevu zaman bloğu hazırlanamadı.");
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                Source = "Public",
                RequestKey = body.RequestKey,
                RequestHash = hash,
                CustomerName = name,
                CustomerPhone = phone,
                CreatedAt = now.UtcDateTime,
                Version = Guid.NewGuid(),
                BookingPolicyVersion = creation.Policy.Version,
                CustomerCancellationLeadMinutes = creation.Policy.CustomerCancellationLeadMinutes,
                RequiresBusinessApproval = creation.Policy.RequiresBusinessApproval
            };
            block.AppointmentId = appointment.Id;
            db.Appointments.Add(appointment); db.AppointmentTimeBlocks.Add(block);
            db.AppointmentAudits.Add(new()
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                AppointmentVersion = appointment.Version,
                OccurredAt = now.UtcDateTime,
                Kind = "PublicCreated"
            });
            item.AppointmentId = appointment.Id; item.ConsumedAt = now; item.ReplayUntil = now.AddHours(24); item.UpdatedAt = now;
            await db.SaveChangesAsync(token);
            // İlk yanıt da PostgreSQL hassasiyetindeki kayıtlı snapshot'tan gelir.
            var response = await ReadAsync(db, appointment.Id, token);
            await transaction.CommitAsync(token);
            return Results.Json(response, statusCode: 201);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: "23P01", ConstraintName: "EX_AppointmentTimeBlocks_StaffTime" } or
        PostgresException { SqlState: "23505", ConstraintName: "IX_Appointments_RequestKey" })
        { return Conflict(); }
        catch (Exception exception) when (exception is PostgresException { SqlState: "55P03" } ||
            exception is InvalidOperationException { InnerException: PostgresException { SqlState: "55P03" } } ||
            exception is DbUpdateException { InnerException: PostgresException { SqlState: "55P03" } } ||
            exception is OperationCanceledException && !ctx.RequestAborted.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "İşlem şu anda tamamlanamıyor. Aynı isteği yeniden deneyin."); }
    }
}
