using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Business;

public static partial class BusinessProfileEndpoints
{
    public sealed record ProfileResponse(string Name, string? Phone, string? Email, string? Address, Guid Version);
    public sealed record UpdateRequest(string Name, string? Phone, string? Email, string? Address, Guid Version);

    public static void MapBusinessProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/business-profile").RequireAuthorization("Owner");
        profile.MapGet("/", async (HttpContext context, AppDbContext db) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            return Results.Ok(await db.BusinessProfiles.AsNoTracking().Where(entry => entry.Id == 1)
                .Select(entry => new ProfileResponse(entry.Name, entry.Phone, entry.Email, entry.Address, entry.Version))
                .SingleAsync(timeout.Token));
        });
        profile.MapPost("/", UpdateAsync);
    }

    private static async Task<IResult> UpdateAsync(UpdateRequest request, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var name = request.Name?.Trim();
        var phone = Optional(request.Phone);
        var email = Optional(request.Email);
        var address = Optional(request.Address);
        if (name is not { Length: >= 2 and <= 150 } || name.Any(char.IsControl))
            return Results.Problem(statusCode: 400, title: "İşletme adı 2–150 karakter olmalı.");
        if (phone is not null)
        {
            if (phone.Length > 32 || !PhoneCharacters().IsMatch(phone))
                return Results.Problem(statusCode: 400, title: "Geçerli bir Türkiye telefon numarası girin.");
            var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
            if (phone.StartsWith('+') && (digits.Length != 12 || !digits.StartsWith("90", StringComparison.Ordinal)))
                return Results.Problem(statusCode: 400, title: "Uluslararası telefon numarası +90 ile başlamalı.");
            if (digits.Length == 11 && digits[0] == '0') digits = digits[1..];
            if (digits.Length == 12 && digits.StartsWith("90", StringComparison.Ordinal)) digits = digits[2..];
            if (!TurkishNumber().IsMatch(digits))
                return Results.Problem(statusCode: 400, title: "Geçerli bir Türkiye cep veya sabit telefon numarası girin.");
            phone = "+90" + digits;
        }
        if (email is not null && (email.Length > 254 || email.Any(char.IsControl) || !new EmailAddressAttribute().IsValid(email)))
            return Results.Problem(statusCode: 400, title: "Geçerli bir iletişim e-postası girin.");
        if (address is not null && (address.Length > 500 || address.Any(character => char.IsControl(character) && character is not '\r' and not '\n')))
            return Results.Problem(statusCode: 400, title: "Adres en fazla 500 karakter olmalı.");
        if (request.Version == Guid.Empty)
            return Results.Problem(statusCode: 400, title: "Profil sürümü gerekli. Bilgileri yeniden yükleyin.");
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId)) return Results.Unauthorized();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        // A revoked Owner session must not save after waiting on another account operation.
        db.ChangeTracker.Clear();
        var owner = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {ownerId} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (owner is null || !owner.TwoFactorEnabled || !await users.IsInRoleAsync(owner, "Owner") ||
            !context.User.HasClaim("amr", "mfa") || await users.IsLockedOutAsync(owner) || owner.SecurityStamp !=
            context.User.FindFirst(users.Options.ClaimsIdentity.SecurityStampClaimType)?.Value)
            return Results.Unauthorized();
        var profile = await db.BusinessProfiles.SingleAsync(entry => entry.Id == 1, timeout.Token);
        if (profile.Version != request.Version) return Conflict();
        profile.Name = name;
        profile.Phone = phone;
        profile.Email = email;
        profile.Address = address;
        profile.Version = Guid.NewGuid();
        db.BusinessProfileAudits.Add(new BusinessProfileAudit
        {
            Id = Guid.NewGuid(),
            ActorId = ownerId,
            ProfileVersion = profile.Version,
            OccurredAt = clock.GetUtcNow()
        });
        try
        {
            await db.SaveChangesAsync(timeout.Token);
            await transaction.CommitAsync(timeout.Token);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        return Results.Ok(new ProfileResponse(profile.Name, profile.Phone, profile.Email, profile.Address, profile.Version));
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Conflict() => Results.Problem(statusCode: 409,
        title: "İşletme profili başka bir işlemde değişti. Güncel bilgileri yükleyip yeniden düzenleyin.");

    [GeneratedRegex(@"\A\+?[0-9 ()-]+\z")]
    private static partial Regex PhoneCharacters();
    [GeneratedRegex(@"\A[2345][0-9]{9}\z")]
    private static partial Regex TurkishNumber();
}
