using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Server.Features.Identity;
using Server.Infrastructure;

namespace Server.Features.Business;

public static class BusinessLogoEndpoints
{
    private const long MaxRequestBytes = 1500000;
    public static void UseBusinessLogoUploadLimit(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.Value?.TrimEnd('/').Equals("/api/business-logo", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.ContentLength > MaxRequestBytes)
            { await Results.Problem(statusCode: 413, title: "Logo isteği çok büyük. En fazla 1 MB dosya seçin.").ExecuteAsync(context); return; }
            // Kestrel chunked istekleri de DTO okunmadan sınırlar.
            var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaxRequestBytes;
        }
        await next(context);
    });
    public sealed record LogoResponse(bool HasLogo, Guid Version, string? ImageUrl, int? Width, int? Height);
    public sealed record UpdateRequest(Guid Version, string? Image);
    public static void MapBusinessLogoEndpoints(this IEndpointRouteBuilder app)
    {
        var logo = app.MapGroup("/api/business-logo");
        logo.MapGet("/", ReadAsync).AllowAnonymous();
        logo.MapGet("/image/{version:guid}", ImageAsync).AllowAnonymous();
        logo.MapPost("/", UpdateAsync).RequireAuthorization("Owner").RequireRateLimiting("business-logo");
    }
    private static CancellationTokenSource Timeout(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
    private static LogoResponse Response(BusinessLogo logo) => new(logo.Png is not null, logo.Version,
        logo.Png is null ? null : $"/api/business-logo/image/{logo.Version:D}", logo.Width, logo.Height);
    private static async Task<IResult> ReadAsync(HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        var logo = await db.BusinessLogos.AsNoTracking().Select(item => new { item.Version, item.Width, item.Height })
            .SingleAsync(timeout.Token);
        return Results.Ok(new LogoResponse(logo.Width is not null, logo.Version,
            logo.Width is null ? null : $"/api/business-logo/image/{logo.Version:D}", logo.Width, logo.Height));
    }
    private static async Task<IResult> ImageAsync(Guid version, HttpContext context, AppDbContext db)
    {
        using var timeout = Timeout(context);
        var bytes = await db.BusinessLogos.AsNoTracking().Where(item => item.Version == version)
            .Select(item => item.Png).SingleOrDefaultAsync(timeout.Token);
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, "image/png");
    }
    private static async Task<IResult> UpdateAsync(UpdateRequest request, HttpContext context, IAntiforgery antiforgery,
        AppDbContext db, UserManager<AppUser> users, TimeProvider clock)
    {
        using var timeout = Timeout(context);
        if (!await AuthEndpoints.HasValidCsrfAsync(antiforgery, context)) return Results.Problem(statusCode: 400, title: "Geçersiz istek doğrulaması.");
        if (request.Version == Guid.Empty) return Results.ValidationProblem(new Dictionary<string, string[]> { ["version"] = ["Güncel logoyu yükleyin."] });
        var image = LogoImage.Normalize(request.Image);
        if (image is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["image"] = ["Geçerli bir PNG veya JPEG seçin: en fazla 1 MB ve 2048 × 2048 piksel."] });
        timeout.Token.ThrowIfCancellationRequested();
        await using var transaction = await db.Database.BeginTransactionAsync(timeout.Token);
        var owner = await OwnerMutationAuthorization.LockAsync(context, db, users, timeout.Token);
        if (owner is null) return Results.Unauthorized();
        var logo = await db.BusinessLogos.FromSqlRaw("SELECT * FROM \"BusinessLogos\" WHERE \"Id\" = 1 FOR UPDATE").SingleAsync(timeout.Token);
        if (logo.Version != request.Version) return Results.Problem(statusCode: 409, title: "Logo başka bir işlemde değişti. Güncel logoyu yükleyin.");
        if (logo.Png is not null && logo.Png.AsSpan().SequenceEqual(image.Png)) return Results.Ok(Response(logo));
        logo.Png = image.Png; logo.Width = image.Width; logo.Height = image.Height; logo.Version = Guid.NewGuid();
        db.BusinessLogoAudits.Add(new BusinessLogoAudit { Id = Guid.NewGuid(), ActorId = owner.Id, LogoVersion = logo.Version, OccurredAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return Results.Ok(Response(logo));
    }
}
