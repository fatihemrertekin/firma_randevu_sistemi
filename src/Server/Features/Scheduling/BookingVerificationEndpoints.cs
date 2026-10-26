using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class BookingVerificationEndpoints
{
    public sealed record Request(Guid RequestKey, Guid ServiceId, Guid ServiceVersion, Guid PolicyVersion,
        Guid StaffMemberId, string? StartsAt, string? CustomerName, string? CustomerPhone);
    public sealed record Resend(Guid ChallengeId, string? ChallengeSecret, Guid ResendKey);
    public sealed record Verify(Guid ChallengeId, string? ChallengeSecret, string? Code);
    public sealed record Requested(Guid ChallengeId, string ChallengeSecret, string MaskedPhone, DateTimeOffset ExpiresAt, DateTimeOffset ResendAt);
    public sealed record Resent(Guid ChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAt);
    public sealed record VerifiedResponse(bool Verified, DateTimeOffset VerifiedUntil);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static void MapBookingVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public-booking/verification").AllowAnonymous().RequireRateLimiting("booking-verification");
        group.MapPost("/request", (HttpContext ctx, IAntiforgery csrf, BookingVerificationSecurity security, AppDbContext db,
            IDataProtectionProvider protection, TimeProvider clock) => ExecuteAsync<Request>(ctx, csrf, security.Enabled && security.DispatchEnabled,
                (body, token) => RequestAsync(body, ctx, db, security, protection, clock, token)));
        group.MapPost("/resend", (HttpContext ctx, IAntiforgery csrf, BookingVerificationSecurity security, AppDbContext db,
            IDataProtectionProvider protection, TimeProvider clock) => ExecuteAsync<Resend>(ctx, csrf, security.Enabled && security.DispatchEnabled,
                (body, token) => ResendAsync(body, ctx, db, security, protection, clock, token)));
        group.MapPost("/verify", (HttpContext ctx, IAntiforgery csrf, BookingVerificationSecurity security, AppDbContext db,
            TimeProvider clock) => ExecuteAsync<Verify>(ctx, csrf, security.Enabled,
                (body, token) => VerifyAsync(body, ctx, db, security, clock, token)));
    }

    internal static async Task<IResult> ExecuteAsync<T>(HttpContext ctx, IAntiforgery csrf, bool enabled,
        Func<T, CancellationToken, Task<IResult>> action)
    {
        using var timeout = AppointmentEndpoints.Timeout(ctx);
        if (ctx.Request.ContentLength > 4096) return Results.Problem(statusCode: 413, title: "İstek gövdesi çok büyük.");
        var origin = ctx.Request.Headers.Origin;
        if (origin.Count != 1 || !Uri.TryCreate(origin[0], UriKind.Absolute, out var uri) ||
            uri.GetLeftPart(UriPartial.Authority) != origin[0] ||
            !string.Equals(origin[0], $"{ctx.Request.Scheme}://{ctx.Request.Host}", StringComparison.OrdinalIgnoreCase) ||
            !await AuthEndpoints.HasValidCsrfAsync(csrf, ctx)) return AuthEndpoints.InvalidCsrf();
        if (!enabled) return Results.Problem(statusCode: 503, title: "Bu işlem şu anda kullanılamıyor.");
        if (!ctx.Request.HasJsonContentType()) return Results.Problem(statusCode: 415, title: "JSON istek gövdesi gerekli.");
        var bytes = new byte[4097]; var count = 0;
        while (count < bytes.Length)
        {
            var read = await ctx.Request.Body.ReadAsync(bytes.AsMemory(count), timeout.Token);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096) return Results.Problem(statusCode: 413, title: "İstek gövdesi çok büyük.");
        T? body;
        try { body = JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), Json); }
        catch (JsonException) { return Invalid(); }
        return body is null ? Invalid() : await action(body, timeout.Token);
    }

    private static IResult Invalid() => Results.Problem(statusCode: 400, title: "Doğrulama isteği geçersiz veya süresi dolmuş.");
    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Doğrulama yetkisi geçersiz.");
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Seçim değişti veya doğrulama isteği daha önce alındı.",
        extensions: new Dictionary<string, object?> { ["code"] = "verification_request_exists_or_changed" });
    private static IResult Limited(HttpContext ctx, DateTimeOffset until, DateTimeOffset now)
    {
        ctx.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling((until - now).TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Problem(statusCode: 429, title: "Doğrulama sınırına ulaşıldı. Belirtilen süreyi bekleyin.");
    }

    internal static bool Secret(BookingVerification? item, string? secret) => item is not null && secret is { Length: 43 } &&
        BookingVerificationSecurity.Equal(item.SecretHash, BookingVerificationSecurity.SecretHash(secret));
    private static bool Active(BookingVerification item, DateTimeOffset now) =>
        item.ConsumedAt is null && item.VerifiedAt is null && item.FailedAttempts < 5 && now < item.ExpiresAt;

    internal static bool TryNormalize(Request body, out string name, out string phone, out DateTime start,
        out Dictionary<string, string[]> errors)
    {
        name = body.CustomerName?.Trim() ?? "";
        phone = ""; start = default; errors = [];
        if (body.RequestKey == Guid.Empty || body.ServiceId == Guid.Empty || body.ServiceVersion == Guid.Empty ||
            body.PolicyVersion == Guid.Empty || body.StaffMemberId == Guid.Empty || !AppointmentRules.TryStart(body.StartsAt, out start)) return false;
        if (name is not { Length: >= 2 and <= 100 } || name.Any(char.IsControl))
            errors["customerName"] = ["Ad 2–100 karakter olmalı ve kontrol karakteri içermemeli."];
        if (!TurkishPhone.TryNormalize(body.CustomerPhone, out phone, out _) || !phone.StartsWith("+905", StringComparison.Ordinal))
            errors["customerPhone"] = ["Geçerli bir Türkiye cep telefonu numarası girin."];
        return errors.Count == 0;
    }

    internal static string RequestHash(Request body, string name, string phone, DateTime start, BookingVerificationSecurity security) =>
        security.Hash("request-v1", body.ServiceId, body.ServiceVersion, body.PolicyVersion, body.StaffMemberId, start, name, phone);

    private static BookingSmsJob Job(BookingVerification item, string phone, string code, IDataProtectionProvider protection, DateTimeOffset now, Guid? resendKey) => new()
    {
        Id = Guid.NewGuid(),
        ChallengeId = item.Id,
        CodeVersion = item.CodeVersion,
        ResendKey = resendKey,
        ProtectedPayload = BookingVerificationSecurity.Protect(protection, phone, code),
        ExpiresAt = item.ExpiresAt,
        ResendAt = now.AddSeconds(60),
        NextAttemptAt = now,
        UpdatedAt = now
    };

    private static async Task<IResult> RequestAsync(Request body, HttpContext ctx, AppDbContext db, BookingVerificationSecurity security,
        IDataProtectionProvider protection, TimeProvider clock, CancellationToken token)
    {
        if (!TryNormalize(body, out var name, out var phone, out var start, out var errors))
            return errors.Count > 0 ? Results.ValidationProblem(errors) : Invalid();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Bu yalnız ön kontroldür; OTP bir slot kilidi veya rezervasyon değildir.
        var service = await db.ServiceDefinitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.ServiceId && x.IsActive && !x.IsDeleted, token);
        var staff = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == body.StaffMemberId && x.IsActive && !x.IsDeleted, token);
        if (service is null || staff is null || !await db.StaffServiceAssignments.AnyAsync(x => x.StaffMemberId == body.StaffMemberId && x.ServiceDefinitionId == body.ServiceId, token)) return Results.NotFound();
        var policy = await db.BookingPolicies.AsNoTracking().SingleAsync(token);
        if (service.Version != body.ServiceVersion || policy.Version != body.PolicyVersion || !AppointmentRules.InWindow(start, now.UtcDateTime, policy)) return Conflict();
        var day = AppointmentCalendar.LocalDate(start);
        var schedule = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(token);
        var available = await AppointmentAvailability.FindAsync(db, staff, service, schedule.IsConfigured, day, now.UtcDateTime, policy,
            service.DurationMinutes, service.BufferBeforeMinutes, service.BufferAfterMinutes, null, token);
        if (!available.StartsAt.Contains(start)) return Conflict();
        await security.LockAsync(db, token);
        now = clock.GetUtcNow();
        if (await db.BookingVerifications.AnyAsync(x => x.RequestKey == body.RequestKey, token))
            return Results.Problem(statusCode: 409, title: "Doğrulama isteği daha önce alındı.", extensions: new Dictionary<string, object?> { ["code"] = "verification_request_exists" });
        var phoneHash = security.Hash("phone", phone);
        var ipHash = security.Hash("ip", ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var wait = await BookingVerificationQuota.SendWaitAsync(db, phoneHash, ipHash, now, token);
        if (wait is not null) return Limited(ctx, wait.Value, now);
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var code = BookingVerificationSecurity.Code();
        var item = new BookingVerification
        {
            Id = Guid.NewGuid(),
            RequestKey = body.RequestKey,
            SecretHash = BookingVerificationSecurity.SecretHash(secret),
            PhoneHash = phoneHash,
            IpHash = ipHash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5),
            ResendAt = now.AddSeconds(60),
            UpdatedAt = now,
            ProtectedPhone = protection.CreateProtector(BookingVerificationSecurity.PayloadPurpose + ".Recipient").Protect(phone),
            RequestHash = RequestHash(body, name, phone, start, security)
        };
        item.CodeHash = security.Hash("code", item.Id, item.CodeVersion, code);
        item.UsedCodeHashes = [security.Hash("code-reuse", item.Id, code)];
        var job = Job(item, phone, code, protection, now, null);
        var reservation = await BookingSmsBudget.ReserveAsync(db, security, job.Id, 1, now, token);
        if (reservation.Attempt is null) return reservation.WaitUntil is { } until ? Limited(ctx, until, now) : Results.Problem(statusCode: 503, title: "Telefon doğrulaması için gönderim bütçesi kullanılamıyor.");
        db.BookingVerifications.Add(item); db.BookingSmsJobs.Add(job);
        BookingVerificationQuota.Event(db, security.KeyId, "Send", phoneHash, ipHash, now);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Results.Json(new Requested(item.Id, secret, "+90******" + phone[^4..], item.ExpiresAt, item.ResendAt), statusCode: 202);
    }

    private static async Task<IResult> ResendAsync(Resend body, HttpContext ctx, AppDbContext db, BookingVerificationSecurity security,
        IDataProtectionProvider protection, TimeProvider clock, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await security.LockAsync(db, token);
        var item = await db.BookingVerifications.SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
        if (!Secret(item, body.ChallengeSecret)) return Forbidden();
        if (item is null) return Forbidden();
        var now = clock.GetUtcNow();
        if (!Active(item, now) || body.ResendKey == Guid.Empty) return Invalid();
        var previous = await db.BookingSmsJobs.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == item.Id && x.ResendKey == body.ResendKey, token);
        if (previous is not null) return Results.Json(new Resent(item.Id, previous.ExpiresAt, previous.ResendAt), statusCode: 202);
        var ipHash = security.Hash("ip", ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var wait = await BookingVerificationQuota.SendWaitAsync(db, item.PhoneHash, ipHash, now, token);
        if (item.ResendAt > now) wait = wait is null || wait < item.ResendAt ? item.ResendAt : wait;
        if (wait is not null) return Limited(ctx, wait.Value, now);
        // Alıcı yalnız şifreli zarf içinde tutulur; teslimden sonra yeniden gönderim için ayrı amaçlı zarf gerekir.
        string phone;
        try { phone = protection.CreateProtector(BookingVerificationSecurity.PayloadPurpose + ".Recipient").Unprotect(item.ProtectedPhone ?? ""); }
        catch (CryptographicException) { return Results.Problem(statusCode: 503, title: "Telefon doğrulaması şu anda kullanılamıyor."); }
        await CancelJobsAsync(db, item.Id, now, token);
        var code = BookingVerificationSecurity.Code();
        while (item.UsedCodeHashes.Contains(security.Hash("code-reuse", item.Id, code))) code = BookingVerificationSecurity.Code();
        item.UsedCodeHashes = [.. item.UsedCodeHashes, security.Hash("code-reuse", item.Id, code)];
        item.CodeVersion++;
        item.CodeHash = security.Hash("code", item.Id, item.CodeVersion, code); item.ResendAt = now.AddSeconds(60); item.UpdatedAt = now;
        var job = Job(item, phone, code, protection, now, body.ResendKey);
        var reservation = await BookingSmsBudget.ReserveAsync(db, security, job.Id, 1, now, token);
        if (reservation.Attempt is null) return reservation.WaitUntil is { } until ? Limited(ctx, until, now) : Results.Problem(statusCode: 503, title: "Telefon doğrulaması için gönderim bütçesi kullanılamıyor.");
        db.BookingSmsJobs.Add(job);
        BookingVerificationQuota.Event(db, security.KeyId, "Send", item.PhoneHash, ipHash, now);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return Results.Json(new Resent(item.Id, item.ExpiresAt, item.ResendAt), statusCode: 202);
    }

    internal static async Task<int> CancelJobsAsync(AppDbContext db, Guid id, DateTimeOffset now, CancellationToken token)
    {
        var jobs = db.BookingSmsJobs.Where(x => x.ChallengeId == id && (x.Status == "Pending" || x.Status == "Processing"));
        await BookingSmsBudget.ReleaseAsync(db, jobs.Select(x => x.Id), token);
        return await jobs
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Cancelled").SetProperty(x => x.ProtectedPayload, (string?)null)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseUntil, (DateTimeOffset?)null).SetProperty(x => x.UpdatedAt, now), token);
    }

    private static async Task<IResult> VerifyAsync(Verify body, HttpContext ctx, AppDbContext db, BookingVerificationSecurity security,
        TimeProvider clock, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await security.LockAsync(db, token);
        var item = await db.BookingVerifications.SingleOrDefaultAsync(x => x.Id == body.ChallengeId, token);
        if (!Secret(item, body.ChallengeSecret) || item is null) return Forbidden();
        var now = clock.GetUtcNow();
        if (item.ConsumedAt is not null) return Invalid();
        if (item.VerifiedUntil > now) return Results.Ok(new VerifiedResponse(true, item.VerifiedUntil.Value));
        if (!Active(item, now)) return Invalid();
        if (body.Code is not { Length: 6 } || body.Code.Any(c => c is < '0' or > '9')) return Invalid();
        var wait = await BookingVerificationQuota.WrongWaitAsync(db, item.PhoneHash, now, token);
        if (wait is not null) return Limited(ctx, wait.Value, now);
        var matches = BookingVerificationSecurity.Equal(item.CodeHash, security.Hash("code", item.Id, item.CodeVersion, body.Code));
        if (matches)
        {
            item.VerifiedAt = now;
            item.VerifiedUntil = now.AddMinutes(5) < item.CreatedAt.AddMinutes(10) ? now.AddMinutes(5) : item.CreatedAt.AddMinutes(10);
            item.CodeHash = null;
            item.ProtectedPhone = null;
            item.UsedCodeHashes = [];
            await CancelJobsAsync(db, item.Id, now, token);
        }
        else
        {
            item.FailedAttempts++;
            BookingVerificationQuota.Event(db, security.KeyId, "Wrong", item.PhoneHash, security.Hash("ip", ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"), now);
            if (item.FailedAttempts == 5) { item.CodeHash = null; item.ProtectedPhone = null; item.UsedCodeHashes = []; await CancelJobsAsync(db, item.Id, now, token); }
        }
        item.UpdatedAt = now;
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return matches && item.VerifiedUntil is { } until ? Results.Ok(new VerifiedResponse(true, until)) : Invalid();
    }
}
