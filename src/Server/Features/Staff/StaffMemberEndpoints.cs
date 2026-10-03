using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Staff;

public static class StaffMemberEndpoints
{
    public sealed record MemberResponse(Guid Id, string Name, bool IsActive, Guid Version);
    public sealed record MemberPage(MemberResponse[] Items, int Page, bool HasMore);
    public sealed record CreateRequest(Guid Id, string Name);
    public sealed record RenameRequest(string Name, Guid Version);
    public sealed record StatusRequest(bool? IsActive, Guid Version);

    public static void MapStaffMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("/api/staff-members").RequireAuthorization("Owner").RequireRateLimiting("staff-management");
        members.MapGet("/", ListAsync);
        members.MapGet("/{id:guid}", ReadAsync);
        members.MapPost("/", CreateAsync);
        members.MapPost("/{id:guid}", RenameAsync);
        members.MapPost("/{id:guid}/status", StatusAsync);
    }

    private static MemberResponse Response(StaffMember member) => new(member.Id, member.Name, member.IsActive, member.Version);
    private static IResult Conflict() => Results.Problem(statusCode: 409, title: "Personel kaydı değişti. Güncel kaydı yükleyin.");
    private static bool ValidName(string? name) => name is { Length: >= 1 and <= 100 } && !name.Any(char.IsControl);
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        using var timeout = Timeout(context);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 50)
            return Results.Problem(statusCode: 400, title: "Geçerli sayfa ve 1–50 arası sayfa boyutu gerekli.");
        var rows = await db.StaffMembers.AsNoTracking().OrderBy(member => member.Name).ThenBy(member => member.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(member => new MemberResponse(member.Id, member.Name, member.IsActive, member.Version)).ToArrayAsync(timeout.Token);
        return Results.Ok(new MemberPage(rows.Take(pageSize).ToArray(), page, rows.Length > pageSize));
    }

    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        var member = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(member => member.Id == id, timeout.Token);
        return member is null ? Results.NotFound() : Results.Ok(Response(member));
    }

    private static void Audit(AppDbContext db, StaffMember member, Guid actorId, string kind, TimeProvider clock) =>
        db.StaffMemberAudits.Add(new StaffMemberAudit
        {
            Id = Guid.NewGuid(),
            StaffMemberId = member.Id,
            ActorId = actorId,
            Kind = kind,
            MemberVersion = member.Version,
            OccurredAt = clock.GetUtcNow()
        });

    private static async Task<IResult> CreateAsync(CreateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        var name = request.Name?.Trim();
        if (!ValidName(name)) return Results.Problem(statusCode: 400, title: "Ad soyad 1–100 karakter olmalı ve kontrol karakteri içermemeli.");
        if (request.Id == Guid.Empty) return Results.Problem(statusCode: 400, title: "Kayıt istek kimliği gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var version = Guid.NewGuid();
        // İki Owner aynı isteği gönderse de DB tek kayıt oluşturur; isim tekil değildir.
        var added = await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"StaffMembers\" (\"Id\", \"Name\", \"IsActive\", \"Version\") VALUES ({request.Id}, {name!}, TRUE, {version}) ON CONFLICT (\"Id\") DO NOTHING", timeout.Token);
        var member = await db.StaffMembers.SingleAsync(member => member.Id == request.Id, timeout.Token);
        if (added == 0) return member.Name == name ? Results.Ok(Response(member)) : Conflict();
        Audit(db, member, owner.Id, "Created", clock);
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Created($"/api/staff-members/{member.Id}", Response(member));
    }

    private static Task<IResult> RenameAsync(Guid id, RenameRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeAsync(id, request.Name, null, request.Version, context, antiforgery, db, users, clock);

    private static Task<IResult> StatusAsync(Guid id, StatusRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock) =>
        ChangeAsync(id, null, request.IsActive, request.Version, context, antiforgery, db, users, clock);

    private static async Task<IResult> ChangeAsync(Guid id, string? name, bool? active, Guid version, HttpContext context,
        IAntiforgery antiforgery, AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context))
            return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        name = name?.Trim();
        if (active is null && !ValidName(name)) return Results.Problem(statusCode: 400, title: "Ad soyad 1–100 karakter olmalı ve kontrol karakteri içermemeli.");
        if (version == Guid.Empty) return Results.Problem(statusCode: 400, title: "Personel sürümü gerekli.");
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (member is null) return Results.NotFound();
        if (member.Version != version) return Conflict();
        if (active is null ? member.Name == name : member.IsActive == active) return Results.Ok(Response(member));
        var kind = active is null ? "Renamed" : active.Value ? "Activated" : "Deactivated";
        if (active.HasValue) member.IsActive = active.Value;
        else member.Name = name!;
        member.Version = Guid.NewGuid();
        Audit(db, member, owner.Id, kind, clock);
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(member));
    }
}
