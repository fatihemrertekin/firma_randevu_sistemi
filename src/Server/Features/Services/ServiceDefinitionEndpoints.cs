using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Services;

public static class ServiceDefinitionEndpoints
{
    // Tutar sözleşmede ondalık metindir; istemcide para hesabı/float yuvarlama yapılmaz.
    public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, string Price, string Currency, bool IsActive, Guid Version);
    public sealed record ServicePage(ServiceResponse[] Items, int Page, bool HasMore);
    public sealed record CreateRequest(Guid Id, string Name, int DurationMinutes, string Price);
    public sealed record UpdateRequest(string Name, int DurationMinutes, string Price, Guid Version);
    public sealed record StatusRequest(bool? IsActive, Guid Version);
    private sealed record Definition(string Name, int DurationMinutes, decimal Price);

    public static void MapServiceDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        var services = app.MapGroup("/api/services").RequireAuthorization("Owner").RequireRateLimiting("service-management");
        services.MapGet("/", ListAsync);
        services.MapGet("/{id:guid}", ReadAsync);
        services.MapPost("/", CreateAsync);
        services.MapPost("/{id:guid}", UpdateAsync);
        services.MapPost("/{id:guid}/status", StatusAsync);
    }
    internal static ServiceResponse Response(ServiceDefinition item) => new(item.Id, item.Name, item.DurationMinutes,
        item.Price.ToString("0.00", CultureInfo.InvariantCulture), item.Currency, item.IsActive, item.Version);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Hizmet kaydı değişti. Güncel kaydı yükleyin.");
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static Definition? Validate(string? name, int minutes, string? price, out Dictionary<string, string[]> errors)
    {
        errors = [];
        name = name?.Trim();
        if (name is not { Length: >= 1 and <= 100 } || name.Any(char.IsControl))
            errors["name"] = ["Hizmet adı 1–100 karakter olmalı ve kontrol karakteri içermemeli."];
        if (minutes is < 1 or > 1440) errors["durationMinutes"] = ["Süre 1–1440 arasında tam dakika olmalı."];
        var amount = 0m;
        if (price is null || !Regex.IsMatch(price, @"\A[0-9]{1,6}(?:\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
            !decimal.TryParse(price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            errors["price"] = ["Fiyat 0–999999,99 TL arasında, en fazla iki ondalık basamaklı olmalı."];
        return errors.Count == 0 ? new Definition(name!, minutes, amount) : null;
    }
    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        using var timeout = Timeout(context);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 50)
            return Results.Problem(statusCode: 400, title: "Geçerli sayfa ve 1–50 arası sayfa boyutu gerekli.");
        var rows = await db.ServiceDefinitions.AsNoTracking().OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1).ToArrayAsync(timeout.Token);
        return Results.Ok(new ServicePage(rows.Take(pageSize).Select(Response).ToArray(), page, rows.Length > pageSize));
    }
    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        var item = await db.ServiceDefinitions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, timeout.Token);
        return item is null ? Results.NotFound() : Results.Ok(Response(item));
    }
    private static void Audit(AppDbContext db, ServiceDefinition item, Guid actorId, string kind, TimeProvider clock) =>
        db.ServiceDefinitionAudits.Add(new ServiceDefinitionAudit
        {
            Id = Guid.NewGuid(),
            ServiceDefinitionId = item.Id,
            ActorId = actorId,
            Kind = kind,
            ServiceVersion = item.Version,
            OccurredAt = clock.GetUtcNow()
        });
    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var definition = Validate(request.Name, request.DurationMinutes, request.Price, out var errors);
        if (definition is null) return Results.ValidationProblem(errors);
        if (request.Id == Guid.Empty) return Results.Problem(statusCode: 400, title: "Kayıt istek kimliği gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var version = Guid.NewGuid();
        var added = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"ServiceDefinitions\" (\"Id\", \"Name\", \"DurationMinutes\", \"Price\", \"Currency\", \"IsActive\", \"Version\") VALUES ({request.Id}, {definition.Name}, {definition.DurationMinutes}, {definition.Price}, 'TRY', TRUE, {version}) ON CONFLICT (\"Id\") DO NOTHING", timeout.Token);
        var item = await db.ServiceDefinitions.SingleAsync(item => item.Id == request.Id, timeout.Token);
        if (added == 0) return Matches(item, definition) ? Results.Ok(Response(item)) : Conflict();
        Audit(db, item, owner.Id, "Created", clock);
        await db.SaveChangesAsync(timeout.Token); await transaction.CommitAsync(timeout.Token);
        return Results.Created($"/api/services/{item.Id}", Response(item));
    }
    private static bool Matches(ServiceDefinition item, Definition definition) =>
        item.Name == definition.Name && item.DurationMinutes == definition.DurationMinutes && item.Price == definition.Price;
    private static Task<IResult> UpdateAsync(Guid id, UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeAsync(id, request, null, request.Version, context, antiforgery, db, users, clock);
    private static Task<IResult> StatusAsync(Guid id, StatusRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeAsync(id, null, request.IsActive, request.Version, context, antiforgery, db, users, clock);
    private static async Task<IResult> ChangeAsync(Guid id, UpdateRequest? request, bool? active, Guid version, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        Definition? definition = null;
        if (request is not null)
        {
            definition = Validate(request.Name, request.DurationMinutes, request.Price, out var errors);
            if (definition is null) return Results.ValidationProblem(errors);
        }
        else if (active is null) return Results.Problem(statusCode: 400, title: "Aktif/pasif durumu gerekli.");
        if (version == Guid.Empty) return Results.Problem(statusCode: 400, title: "Hizmet sürümü gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var item = await db.ServiceDefinitions.FromSqlInterpolated($"SELECT * FROM \"ServiceDefinitions\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(timeout.Token);
        if (item is null) return Results.NotFound();
        if (item.Version != version) return Conflict();
        if (definition is not null ? Matches(item, definition) : item.IsActive == active) return Results.Ok(Response(item));
        if (definition is not null) { item.Name = definition.Name; item.DurationMinutes = definition.DurationMinutes; item.Price = definition.Price; }
        else item.IsActive = active!.Value;
        item.Version = Guid.NewGuid();
        Audit(db, item, owner.Id, definition is not null ? "Updated" : item.IsActive ? "Activated" : "Deactivated", clock);
        await db.SaveChangesAsync(timeout.Token); await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(item));
    }
}
