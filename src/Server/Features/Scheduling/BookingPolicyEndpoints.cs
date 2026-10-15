using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Scheduling;

public static class BookingPolicyEndpoints
{
    public sealed record Response(Guid Version, int LeadMinutes, int HorizonDays, int SlotMinutes, string TimeZone);
    public sealed record UpdateRequest(Guid Version, int? LeadMinutes, int? HorizonDays, int? SlotMinutes);

    public static void MapBookingPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var policy = app.MapGroup("/api/booking-policy").RequireAuthorization("Owner").RequireRateLimiting("booking-policy");
        policy.MapGet("/", ReadAsync);
        policy.MapPost("/", UpdateAsync);
    }

    private static Response Body(BookingPolicy policy) => new(policy.Version, policy.LeadMinutes, policy.HorizonDays, policy.SlotMinutes, "Europe/Istanbul");

    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        return Results.Ok(Body(await db.BookingPolicies.AsNoTracking().SingleAsync(timeout.Token)));
    }

    private static async Task<IResult> UpdateAsync(UpdateRequest request, HttpContext context, IAntiforgery csrf,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = AppointmentEndpoints.Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(csrf, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var errors = new Dictionary<string, string[]>();
        if (request.Version == Guid.Empty) errors["version"] = ["Güncel ayar sürümü gerekli."];
        if (request.LeadMinutes is not (>= 0 and <= 43200)) errors["leadMinutes"] = ["Ön süre 0–43200 dakika arasında olmalı."];
        if (request.HorizonDays is not (>= 1 and <= 365)) errors["horizonDays"] = ["İleri rezervasyon süresi 1–365 gün arasında olmalı."];
        if (request.SlotMinutes is not (>= 1 and <= 60)) errors["slotMinutes"] = ["Başlangıç adımı 1–60 dakika arasında olmalı."];
        if (!errors.ContainsKey("leadMinutes") && !errors.ContainsKey("horizonDays") && request.LeadMinutes >= request.HorizonDays * 1440)
            errors["leadMinutes"] = ["Ön süre ileri rezervasyon süresinden kısa olmalı."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        // Oluşturma ve taşıma aynı satırı FOR SHARE ile okur; yeni kural kayıt anında geçerli olur.
        var policy = await db.BookingPolicies.FromSqlRaw("SELECT * FROM \"BookingPolicies\" WHERE \"Id\" = 1 FOR UPDATE").SingleAsync(timeout.Token);
        if (policy.Version != request.Version) return Results.Problem(statusCode: 409, title: "Rezervasyon ayarları değişti. Güncel ayarları yükleyin.");
        if (policy.LeadMinutes == request.LeadMinutes && policy.HorizonDays == request.HorizonDays && policy.SlotMinutes == request.SlotMinutes) return Results.Ok(Body(policy));
        var previous = policy.Version;
        policy.LeadMinutes = request.LeadMinutes.GetValueOrDefault(); policy.HorizonDays = request.HorizonDays.GetValueOrDefault(); policy.SlotMinutes = request.SlotMinutes.GetValueOrDefault(); policy.Version = Guid.NewGuid();
        db.BookingPolicyAudits.Add(new() { Id = Guid.NewGuid(), ActorId = owner.Id, PreviousVersion = previous, PolicyVersion = policy.Version, OccurredAt = clock.GetUtcNow(), LeadMinutes = policy.LeadMinutes, HorizonDays = policy.HorizonDays, SlotMinutes = policy.SlotMinutes });
        await db.SaveChangesAsync(timeout.Token); await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Body(policy));
    }
}
