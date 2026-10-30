using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace SubscriptionOperator;

internal static class SubscriptionLedger
{
    internal sealed record PeriodInput(string TargetId, Guid RequestKey, DateOnly StartsOn, DateOnly EndsOn, DateOnly DueOn, string AmountTry);
    internal sealed record PaymentInput(string TargetId, Guid RequestKey, Guid PeriodId, Guid Version, DateOnly PaidOn, string AmountTry, string Reference);
    internal sealed record ReverseInput(string TargetId, Guid RequestKey, Guid PeriodId, Guid Version, string Reason);
    internal sealed record CorrectionInput(string TargetId, Guid RequestKey, Guid PeriodId, Guid Version, DateOnly PaidOn, string AmountTry, string Reason);
    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static bool Amount(string? value, out decimal amount)
    {
        amount = 0;
        return value is { Length: <= 13 } && Regex.IsMatch(value, @"^[0-9]{1,10}(\.[0-9]{1,2})?$", RegexOptions.CultureInvariant) &&
            decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && amount is > 0 and <= 9999999999.99m;
    }
    private static string Hash(Guid actor, string kind, object input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { actor, kind, input })));
    private static IResult Conflict(string title) => Results.Problem(statusCode: 409, title: title);
    private static IResult TargetRequired() => Results.Problem(statusCode: 409, title: "Firma seçimi gerekli. Çok firma ekranı bağlanana kadar kayıt yapılamaz.",
        extensions: new Dictionary<string, object?> { ["code"] = "subscription_target_required" });
    private static IResult Invalid(string title) => Results.Problem(statusCode: 400, title: title);

    internal static void MapSubscriptionLedger(this WebApplication app)
    {
        MapLedger(app.MapGroup("/api/subscription"));
        MapLedger(app.MapGroup("/api/targets/{targetId}/subscription"));
    }

    private static void MapLedger(RouteGroupBuilder routes)
    {
        var group = routes.RequireAuthorization("Operator").RequireRateLimiting("ledger");
        group.MapGet("/", ReadAsync);
        group.MapPost("/periods", (PeriodInput request, HttpContext c, IAntiforgery a, SubscriptionTargets targets, UserManager<OperatorUser> users, TimeProvider clock) =>
            WriteAsync(request.TargetId, c, a, targets, users, (db, actor, token) => AddPeriodAsync(request, db, actor, clock, token)));
        group.MapPost("/payments", (PaymentInput request, HttpContext c, IAntiforgery a, SubscriptionTargets targets, UserManager<OperatorUser> users, TimeProvider clock) =>
            WriteAsync(request.TargetId, c, a, targets, users, (db, actor, token) => AddPaymentAsync(request, db, actor, clock, token)));
        group.MapPost("/payments/{id:guid}/reverse", (Guid id, ReverseInput request, HttpContext c, IAntiforgery a, SubscriptionTargets targets, UserManager<OperatorUser> users, TimeProvider clock) =>
            WriteAsync(request.TargetId, c, a, targets, users, (db, actor, token) => ReverseAsync(id, request, db, actor, clock, token)));
        group.MapPost("/payments/{id:guid}/correct", (Guid id, CorrectionInput request, HttpContext c, IAntiforgery a, SubscriptionTargets targets, UserManager<OperatorUser> users, TimeProvider clock) =>
            WriteAsync(request.TargetId, c, a, targets, users, (db, actor, token) => CorrectAsync(id, request, db, actor, clock, token)));
    }

    private static async Task<IResult> ReadAsync(HttpContext c, SubscriptionTargets targets, TimeProvider clock,
        int page = 1, Guid? periodId = null, int paymentsPage = 1)
    {
        var routeTarget = c.Request.RouteValues["targetId"] as string;
        var target = routeTarget is null ? targets.Single : targets.Find(routeTarget);
        if (target is null) return routeTarget is null ? TargetRequired() : Results.NotFound();
        if (page is < 1 or > 1000000 || paymentsPage is < 1 or > 1000000 || c.Request.Query.Keys.Any(k => k is not ("page" or "periodId" or "paymentsPage"))) return Invalid("Sayfa bilgilerini kontrol edin.");
        var checkedAt = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(checkedAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await using var db = await target.OpenAsync(timeout.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, timeout.Token);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", timeout.Token);
            var balances = db.SubscriptionPeriods.AsNoTracking().Select(p => new
            {
                p.Id,
                p.StartsOn,
                p.EndsOn,
                p.DueOn,
                p.Version,
                p.AmountTry,
                Paid = db.SubscriptionPayments.Where(x => x.PeriodId == p.Id).Sum(x => (decimal?)x.AmountTry) ?? 0m
            });
            var summary = await SubscriptionSummary.ReadAsync(db, today, timeout.Token);
            var total = await db.SubscriptionPeriods.CountAsync(timeout.Token);
            page = Math.Min(page, Math.Max(1, (total + 11) / 12));
            if (periodId is { } requestedId)
            {
                var requestedStart = await db.SubscriptionPeriods.Where(p => p.Id == requestedId).Select(p => (DateOnly?)p.StartsOn).SingleOrDefaultAsync(timeout.Token);
                if (requestedStart is null) return Results.NotFound();
                page = await db.SubscriptionPeriods.CountAsync(p => p.StartsOn > requestedStart.Value, timeout.Token) / 12 + 1;
            }
            var periods = await balances.OrderByDescending(p => p.StartsOn).Skip((page - 1) * 12).Take(12).ToArrayAsync(timeout.Token);
            var selected = periodId ?? periods.FirstOrDefault(p => p.StartsOn <= today && today < p.EndsOn)?.Id ?? periods.FirstOrDefault()?.Id;
            var paymentsQuery = db.SubscriptionPayments.AsNoTracking().Where(p => p.PeriodId == selected);
            var paymentCount = await paymentsQuery.CountAsync(timeout.Token);
            paymentsPage = Math.Min(paymentsPage, Math.Max(1, (paymentCount + 19) / 20));
            var payments = await paymentsQuery.OrderByDescending(p => p.RecordedAt).ThenByDescending(p => p.Id).Skip((paymentsPage - 1) * 20).Take(20)
                .Select(p => new
                {
                    p.Id,
                    p.PaidOn,
                    p.AmountTry,
                    p.Reference,
                    p.Reason,
                    p.ReversesPaymentId,
                    p.ReplacesPaymentId,
                    p.RecordedBy,
                    p.RecordedAt,
                    Reversed = db.SubscriptionPayments.Any(x => x.ReversesPaymentId == p.Id)
                }).ToArrayAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
            return Results.Ok(new
            {
                targetId = target.Id,
                targetName = target.Name,
                asOf = today,
                checkedAt,
                timeZone = "Europe/Istanbul",
                currency = "TRY",
                summary,
                page,
                total,
                selectedPeriodId = selected,
                paymentsPage,
                paymentCount,
                periods = periods.Select(p => new { p.Id, p.StartsOn, p.EndsOn, p.DueOn, p.Version, amountTry = Money(p.AmountTry), paidTry = Money(p.Paid), outstandingTry = Money(p.AmountTry - p.Paid), overdue = p.StartsOn <= today && today > p.DueOn && p.Paid < p.AmountTry }),
                payments = payments.Select(p => new { p.Id, p.PaidOn, amountTry = Money(p.AmountTry), p.Reference, p.Reason, p.ReversesPaymentId, p.ReplacesPaymentId, p.RecordedBy, p.RecordedAt, p.Reversed })
            });
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException || e is OperationCanceledException && !c.RequestAborted.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "Firma kayıtları okunamadı. Hedefi ve bağlantıyı kontrol edip yeniden deneyin."); }
    }

    private static async Task<IResult> WriteAsync(string targetId, HttpContext c, IAntiforgery a, SubscriptionTargets targets, UserManager<OperatorUser> users,
        Func<AppDbContext, Guid, CancellationToken, Task<IResult>> action)
    {
        if (!await OperatorIdentity.Csrf(c, a)) return OperatorIdentity.InvalidCsrf();
        var routeTarget = c.Request.RouteValues["targetId"] as string;
        var target = routeTarget is null ? targets.Single : targets.Find(routeTarget);
        if (target is null) return routeTarget is null ? TargetRequired() : Results.NotFound();
        if (targetId != target.Id) return Results.NotFound();
        if (!Guid.TryParse(users.GetUserId(c.User), out var actor) || actor == Guid.Empty) return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await using var db = await target.OpenAsync(timeout.Token);
            await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
            // Düşük hacimli tek firma mali işlemleri aynı kilidi alır; DB trigger'ları diğer yazıcıları da korur.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Server.Features.Subscriptions.SubscriptionAccess.LedgerLock})", timeout.Token);
            var result = await action(db, actor, timeout.Token);
            if (result is not Microsoft.AspNetCore.Http.IStatusCodeHttpResult status || status.StatusCode is >= 200 and < 300)
                await transaction.CommitAsync(timeout.Token);
            return result;
        }
        catch (Exception e) when ((e.GetBaseException() as PostgresException)?.SqlState is "23505" or "23514" or "23P01" or "40001" or "40P01")
        { return Conflict("Kayıt değişmiş, dönem çakışmış veya ödeme daha önce kaydedilmiş. Güncel bilgileri yükleyin."); }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException || e is OperationCanceledException && !c.RequestAborted.IsCancellationRequested)
        { return Results.Problem(statusCode: 503, title: "İşlem sonucu doğrulanamadı. Aynı işlemle yeniden deneyin; ikinci bir ödeme oluşturmayın."); }
    }

    private static async Task<IResult> AddPeriodAsync(PeriodInput r, AppDbContext db, Guid actor, TimeProvider clock, CancellationToken token)
    {
        if (r.RequestKey == Guid.Empty || !Amount(r.AmountTry, out var amount) || r.StartsOn.Year == 9999 && r.StartsOn.Month == 12 ||
            r.EndsOn != new DateOnly(r.StartsOn.Year, r.StartsOn.Month, 1).AddMonths(1)) return Invalid("Dönem tarihlerini ve pozitif, en fazla iki ondalıklı TL tutarını kontrol edin.");
        var old = await db.SubscriptionPeriods.SingleOrDefaultAsync(p => p.Id == r.RequestKey, token);
        if (old is not null) return old.RecordedBy == actor && old.StartsOn == r.StartsOn && old.EndsOn == r.EndsOn && old.DueOn == r.DueOn && old.AmountTry == amount
            ? Results.Ok(new { old.Id }) : Conflict("İşlem anahtarı başka bir dönem için kullanılmış.");
        if (r.StartsOn.Day != 1 && await db.SubscriptionPeriods.AnyAsync(token)) return Invalid("Yalnız ilk dönem kısmi ay olabilir. Sonraki dönem ayın ilk günü başlamalı.");
        var period = new SubscriptionPeriod { Id = r.RequestKey, StartsOn = r.StartsOn, EndsOn = r.EndsOn, DueOn = r.DueOn, AmountTry = amount, RecordedBy = actor, RecordedAt = clock.GetUtcNow().UtcDateTime };
        db.SubscriptionPeriods.Add(period); await db.SaveChangesAsync(token); return Results.Ok(new { period.Id });
    }

    private static async Task<IResult?> ReplayAsync(AppDbContext db, Guid key, string hash, CancellationToken token)
    {
        if (key == Guid.Empty) return Invalid("İşlem anahtarı gerekli.");
        var old = await db.SubscriptionPayments.AsNoTracking().SingleOrDefaultAsync(p => p.RequestKey == key, token);
        return old is null ? null : old.RequestHash == hash ? Results.Ok(new { old.Id }) : Conflict("İşlem anahtarı başka içerikle kullanılmış.");
    }
    private static async Task<IResult> AddPaymentAsync(PaymentInput r, AppDbContext db, Guid actor, TimeProvider clock, CancellationToken token)
    {
        var hash = Hash(actor, "payment", r); var replay = await ReplayAsync(db, r.RequestKey, hash, token); if (replay is not null) return replay;
        if (!Amount(r.AmountTry, out var amount) || r.PaidOn > Today(clock) || r.Reference is null ||
            !Regex.IsMatch(r.Reference, "^[A-Za-z0-9][A-Za-z0-9._:/-]{0,79}$", RegexOptions.CultureInvariant)) return Invalid("Ödeme tarihi, tutarı ve işlem referansını kontrol edin.");
        var period = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT * FROM \"SubscriptionPeriods\" WHERE \"Id\"={r.PeriodId} FOR UPDATE").SingleOrDefaultAsync(token);
        if (period is null) return Results.NotFound(); if (period.Version != r.Version) return Conflict("Dönem değişti. Güncel bilgileri yükleyin.");
        var payment = new SubscriptionPayment { PeriodId = period.Id, AmountTry = amount, PaidOn = r.PaidOn, Reference = r.Reference, RequestKey = r.RequestKey, RequestHash = hash, RecordedBy = actor, RecordedAt = clock.GetUtcNow().UtcDateTime };
        db.SubscriptionPayments.Add(payment); await db.SaveChangesAsync(token); return Results.Ok(new { payment.Id });
    }
    private static bool Reason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 300;
    private static SubscriptionPayment Reversal(SubscriptionPayment original, Guid actor, Guid key, string hash, string reason, TimeProvider clock) => new()
    {
        PeriodId = original.PeriodId,
        AmountTry = -original.AmountTry,
        PaidOn = Today(clock),
        ReversesPaymentId = original.Id,
        Reason = reason,
        RequestKey = key,
        RequestHash = hash,
        RecordedBy = actor,
        RecordedAt = clock.GetUtcNow().UtcDateTime
    };
    private static async Task<IResult> ReverseAsync(Guid id, ReverseInput r, AppDbContext db, Guid actor, TimeProvider clock, CancellationToken token)
    {
        var hash = Hash(actor, "reverse:" + id, r); var replay = await ReplayAsync(db, r.RequestKey, hash, token); if (replay is not null) return replay;
        if (!Reason(r.Reason)) return Invalid("Düzeltme gerekçesi gerekli; en fazla 300 karakter yazın.");
        var original = await db.SubscriptionPayments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.AmountTry > 0, token);
        if (original is null || original.PeriodId != r.PeriodId) return Results.NotFound();
        var period = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT * FROM \"SubscriptionPeriods\" WHERE \"Id\"={r.PeriodId} FOR UPDATE").SingleAsync(token);
        if (period.Version != r.Version) return Conflict("Dönem değişti. Güncel bilgileri yükleyin.");
        var reversal = Reversal(original, actor, r.RequestKey, hash, r.Reason.Trim(), clock);
        db.SubscriptionPayments.Add(reversal); await db.SaveChangesAsync(token); return Results.Ok(new { reversal.Id });
    }
    private static async Task<IResult> CorrectAsync(Guid id, CorrectionInput r, AppDbContext db, Guid actor, TimeProvider clock, CancellationToken token)
    {
        var hash = Hash(actor, "correct:" + id, r); var replay = await ReplayAsync(db, r.RequestKey, hash, token); if (replay is not null) return replay;
        if (!Reason(r.Reason) || !Amount(r.AmountTry, out var amount) || r.PaidOn > Today(clock)) return Invalid("Düzeltme tarihi, tutarı ve gerekçesini kontrol edin.");
        var original = await db.SubscriptionPayments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.AmountTry > 0, token);
        if (original is null) return Results.NotFound();
        var locked = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT * FROM \"SubscriptionPeriods\" WHERE \"Id\"={r.PeriodId} OR \"Id\"={original.PeriodId} ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(token);
        var targetPeriod = locked.SingleOrDefault(p => p.Id == r.PeriodId);
        if (targetPeriod is null) return Results.NotFound();
        if (targetPeriod.Version != r.Version) return Conflict("Dönem değişti. Güncel bilgileri yükleyin.");
        var reverseKey = new Guid(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { r.RequestKey, kind = "correction-reversal" })).AsSpan(0, 16));
        db.SubscriptionPayments.Add(Reversal(original, actor, reverseKey, hash, r.Reason.Trim(), clock)); await db.SaveChangesAsync(token);
        var replacement = new SubscriptionPayment
        {
            PeriodId = r.PeriodId,
            AmountTry = amount,
            PaidOn = r.PaidOn,
            Reference = original.Reference,
            ReplacesPaymentId = original.Id,
            Reason = r.Reason.Trim(),
            RequestKey = r.RequestKey,
            RequestHash = hash,
            RecordedBy = actor,
            RecordedAt = clock.GetUtcNow().UtcDateTime
        };
        db.SubscriptionPayments.Add(replacement); await db.SaveChangesAsync(token); return Results.Ok(new { replacement.Id });
    }
}
