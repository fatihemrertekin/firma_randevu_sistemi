using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Server.Infrastructure;

namespace Server.Features.Identity;

public static class OwnerRecoveryEmailEndpoints
{
    public sealed record StatusResponse(string Email, DateTimeOffset? VerifiedAt, bool DeliveryAvailable);
    public sealed record ConfirmRequest(string Token);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string TokenHash(string token, IConfiguration configuration) => Hash(configuration["Auth:InstanceId"] + "\n" + token);
    private static bool MatchesEmail(OwnerRecoveryEmail record, AppUser owner) =>
        record.EmailHash == Hash(owner.NormalizedEmail ?? "");
    private static IResult InvalidToken() => Results.Problem(statusCode: 400, title: "Doğrulama bağlantısı geçersiz veya süresi dolmuş.");

    public static void MapOwnerRecoveryEmailEndpoints(this RouteGroupBuilder auth)
    {
        var group = auth.MapGroup("/recovery-email").RequireAuthorization("Owner");
        group.MapGet("/", ReadAsync);
        group.MapPost("/request", RequestAsync).RequireRateLimiting("login");
        auth.MapPost("/recovery-email/confirm", ConfirmAsync).AllowAnonymous().RequireRateLimiting("login");
    }

    private static async Task<IResult> ReadAsync(HttpContext context, UserManager<AppUser> users,
        AppDbContext db, IOwnerEmailVerificationDelivery delivery)
    {
        context.Response.Headers.CacheControl = "no-store";
        var owner = await users.GetUserAsync(context.User);
        if (owner is null || !owner.TwoFactorEnabled) return Results.Unauthorized();
        var record = await db.OwnerRecoveryEmails.AsNoTracking().SingleOrDefaultAsync(
            entry => entry.OwnerId == owner.Id, context.RequestAborted);
        return Results.Ok(new StatusResponse(owner.Email ?? "", record is not null && MatchesEmail(record, owner)
            ? record.VerifiedAt : null, delivery.CanDeliver(owner.Email ?? "")));
    }

    private static async Task<IResult> RequestAsync(HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, IOwnerEmailVerificationDelivery delivery,
        IConfiguration configuration, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var ownerId = users.GetUserId(context.User);
        if (!Guid.TryParse(ownerId, out var id)) return Results.Unauthorized();
        string token;
        string email;
        DateTimeOffset expiresAt;
        await using (var transaction = await db.Database.BeginTransactionAsync(timeout.Token))
        {
            var owner = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync(timeout.Token);
            if (owner is null || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
                owner.SecurityStamp != context.User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType))
                return Results.Unauthorized();
            email = owner.Email ?? "";
            if (!delivery.CanDeliver(email)) return Results.Problem(statusCode: 503, title: "E-posta doğrulama gönderimi şu anda kullanılamıyor.");
            var record = await db.OwnerRecoveryEmails.SingleOrDefaultAsync(entry => entry.OwnerId == id, timeout.Token);
            if (record is not null && MatchesEmail(record, owner) && record.VerifiedAt is not null)
                return Results.Problem(statusCode: 409, title: "Hesap e-postanız zaten doğrulanmış.");
            var now = clock.GetUtcNow();
            if (record is not null && now < record.RequestedAt.AddSeconds(60))
                return Results.Problem(statusCode: 429, title: "Yeni doğrulama istemeden önce bir dakika bekleyin.");
            token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            expiresAt = now.AddMinutes(30);
            if (record is null)
            {
                record = new OwnerRecoveryEmail { OwnerId = id, EmailHash = "", StampHash = "" };
                db.OwnerRecoveryEmails.Add(record);
            }
            record.EmailHash = Hash(owner.NormalizedEmail ?? "");
            record.StampHash = Hash(owner.SecurityStamp ?? "");
            record.TokenHash = TokenHash(token, configuration);
            record.RequestedAt = now;
            record.ExpiresAt = expiresAt;
            record.VerifiedAt = null;
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        // Delivery runs after the transaction. A failure never returns a sent notice.
        try { await delivery.DeliverAsync(email, token, expiresAt, timeout.Token); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await db.OwnerRecoveryEmails.Where(entry => entry.OwnerId == id &&
                entry.TokenHash == TokenHash(token, configuration) && entry.VerifiedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.TokenHash, (string?)null), cleanup.Token);
            return Results.Problem(statusCode: 503, title: "Doğrulama iletisi teslim edilemedi. Bir dakika sonra yeniden deneyin.");
        }
        return Results.Ok(new { expiresAt });
    }

    private static async Task<IResult> ConfirmAsync(ConfirmRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, IConfiguration configuration, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (request.Token is not { Length: 43 } || request.Token.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) return InvalidToken();
        var hash = TokenHash(request.Token, configuration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var grant = await db.OwnerRecoveryEmails.AsNoTracking().SingleOrDefaultAsync(entry => entry.TokenHash == hash, timeout.Token);
        if (grant is null) return InvalidToken();
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {grant.OwnerId} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        var record = await db.OwnerRecoveryEmails.SingleAsync(entry => entry.OwnerId == grant.OwnerId, timeout.Token);
        if (owner is null || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
            !MatchesEmail(record, owner) || record.StampHash != Hash(owner.SecurityStamp ?? "") ||
            record.TokenHash != hash || record.VerifiedAt is not null || clock.GetUtcNow() >= record.ExpiresAt) return InvalidToken();
        record.VerifiedAt = clock.GetUtcNow();
        record.TokenHash = null;
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.NoContent();
    }
}
