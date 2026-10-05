using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Features.Services;
using Server.Infrastructure;

namespace Server.Features.Staff;

public static class StaffServiceEndpoints
{
    public sealed record ServiceReference(Guid Id, Guid Version);
    public sealed record SelectionResponse(StaffMemberEndpoints.MemberResponse Member, ServiceReference[] Selected);
    public sealed record SelectionPage(StaffMemberEndpoints.MemberResponse Member, ServiceReference[] Selected,
        ServiceDefinitionEndpoints.ServiceResponse[] Items, int Page, bool HasMore);
    public sealed record UpdateRequest(Guid Version, ServiceReference[]? Services);

    internal static void MapStaffServiceEndpoints(this RouteGroupBuilder members)
    {
        // Çağıran grup MFA Owner ve staff-management limitini uygular.
        members.MapGet("/{id:guid}/services", ReadAsync);
        members.MapPost("/{id:guid}/services", UpdateAsync);
    }
    private static StaffMemberEndpoints.MemberResponse Member(StaffMember item) => new(item.Id, item.Name, item.IsActive, item.Version);
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static IResult Conflict(string title = "Personel veya hizmet değişti. Güncel seçimleri yükleyin.") => Results.Problem(statusCode: 409, title: title);
    private static Task<ServiceReference[]> SelectedAsync(AppDbContext db, Guid memberId, CancellationToken token) =>
        (from link in db.StaffServiceAssignments.AsNoTracking()
         join service in db.ServiceDefinitions.AsNoTracking() on link.ServiceDefinitionId equals service.Id
         where link.StaffMemberId == memberId && !service.IsDeleted
         orderby service.Id
         select new ServiceReference(service.Id, service.Version)).ToArrayAsync(token);

    private static async Task<IResult> ReadAsync(Guid id, HttpContext context, AppDbContext db, int page = 1, int pageSize = 20)
    {
        using var timeout = Timeout(context);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 50)
            return Results.Problem(statusCode: 400, title: "Geçerli sayfa ve 1–50 arası sayfa boyutu gerekli.");
        // Personel sürümü, tam seçim ve katalog aynı snapshot'tan gelir.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, timeout.Token);
        var member = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && !item.IsDeleted, timeout.Token);
        if (member is null) return Results.NotFound();
        var selected = await SelectedAsync(db, id, timeout.Token);
        var rows = await db.ServiceDefinitions.AsNoTracking().Where(item => !item.IsDeleted).OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize + 1).ToArrayAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(new SelectionPage(Member(member), selected, rows.Take(pageSize).Select(ServiceDefinitionEndpoints.Response).ToArray(), page, rows.Length > pageSize));
    }
    private static async Task<IResult> UpdateAsync(Guid id, UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (request.Version == Guid.Empty || request.Services is null || request.Services.Length > 500 ||
            request.Services.Any(item => item is null || item.Id == Guid.Empty || item.Version == Guid.Empty) ||
            request.Services.Select(item => item.Id).Distinct().Count() != request.Services.Length)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["services"] = ["Geçerli personel/hizmet sürümleri ve en fazla 500 farklı hizmet seçimi gerekli."] });
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var member = await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(timeout.Token);
        if (member is null || member.IsDeleted) return Results.NotFound();
        if (member.Version != request.Version) return Conflict();
        var allLinks = await db.StaffServiceAssignments.Where(item => item.StaffMemberId == id).ToArrayAsync(timeout.Token);
        var desired = request.Services.Select(item => item.Id).ToHashSet();
        var ids = desired.Union(allLinks.Select(item => item.ServiceDefinitionId)).Order().ToArray();
        var lockedServices = await db.ServiceDefinitions.FromSqlInterpolated($"SELECT * FROM \"ServiceDefinitions\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE")
            .ToArrayAsync(timeout.Token);
        // Silme ve seçim değişikliği aynı hizmet kilitlerini kullanır; tarihsel bağlar korunur.
        var links = allLinks.Where(link => lockedServices.Any(service => service.Id == link.ServiceDefinitionId && !service.IsDeleted)).ToArray();
        var previous = links.Select(item => item.ServiceDefinitionId).ToHashSet();
        var services = lockedServices.Where(service => desired.Contains(service.Id)).ToArray();
        if (services.Length != desired.Count || services.Any(service => service.IsDeleted)) return Results.Problem(statusCode: 404, title: "Seçilen hizmet bulunamadı. Güncel seçimleri yükleyin.");
        if (services.Any(service => service.Version != request.Services.Single(item => item.Id == service.Id).Version)) return Conflict();
        if (services.Any(service => !previous.Contains(service.Id) && (!member.IsActive || !service.IsActive)))
            return Conflict("Yeni eşleşme için personel ve hizmet aktif olmalı. Güncel seçimleri yükleyin.");
        if (previous.SetEquals(desired)) return Results.Ok(new SelectionResponse(Member(member), await SelectedAsync(db, id, timeout.Token)));
        member.Version = Guid.NewGuid();
        foreach (var removed in links.Where(item => !desired.Contains(item.ServiceDefinitionId)))
        {
            db.StaffServiceAssignments.Remove(removed);
            Audit(db, id, removed.ServiceDefinitionId, owner.Id, member.Version, "Unassigned", clock);
        }
        foreach (var added in desired.Except(previous))
        {
            db.StaffServiceAssignments.Add(new StaffServiceAssignment { StaffMemberId = id, ServiceDefinitionId = added });
            Audit(db, id, added, owner.Id, member.Version, "Assigned", clock);
        }
        await db.SaveChangesAsync(timeout.Token);
        var result = new SelectionResponse(Member(member), await SelectedAsync(db, id, timeout.Token));
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(result);
    }
    private static void Audit(AppDbContext db, Guid memberId, Guid serviceId, Guid actorId, Guid version, string kind, TimeProvider clock) =>
        db.StaffServiceAssignmentAudits.Add(new StaffServiceAssignmentAudit
        {
            Id = Guid.NewGuid(),
            StaffMemberId = memberId,
            ServiceDefinitionId = serviceId,
            ActorId = actorId,
            MemberVersion = version,
            Kind = kind,
            OccurredAt = clock.GetUtcNow()
        });
}
