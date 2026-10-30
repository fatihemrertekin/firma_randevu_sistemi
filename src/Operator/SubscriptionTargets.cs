using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SubscriptionOperator;

public sealed class SubscriptionTargets
{
    private readonly Dictionary<string, SubscriptionTarget> targets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim summarySlots = new(3);
    public SubscriptionTarget? Single => targets.Count == 1 ? targets.Values.Single() : null;

    public SubscriptionTargets(IConfiguration config, IHostEnvironment environment)
    {
        var section = config.GetSection("SubscriptionTargets");
        if (!section.Exists())
        {
            var single = new SubscriptionTarget(config);
            targets.Add(single.Id, single);
            return;
        }
        if (section.Value is not null || config["SubscriptionTarget:InstanceId"] is not null || config["SubscriptionTarget:Name"] is not null || config.GetConnectionString("SubscriptionTarget") is not null)
            throw new InvalidOperationException("Tek firma ve çok firma ayarlarını birlikte kullanmayın.");
        var destinations = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in section.GetChildren())
        {
            // Şifresiz istisna yalnız Development'ta sayısal loopback test DB'sidir; DNS adı istisna yaratmaz.
            var connection = item["ConnectionString"];
            bool localTest;
            try
            {
                var host = new NpgsqlConnectionStringBuilder(connection).Host;
                localTest = environment.IsDevelopment() && IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
            }
            catch (ArgumentException) { throw new InvalidOperationException("Firma bağlantı yapılandırması geçersiz; sunucu ayarlarını kontrol edin."); }
            var target = new SubscriptionTarget(item["InstanceId"], item["Name"], connection,
                config.GetConnectionString("OperatorIdentity"), requireTls: !localTest);
            if (!ids.Add(target.Id) || !destinations.Add(target.Destination))
                throw new InvalidOperationException("Firma kurulum kodları ve DB hedefleri benzersiz olmalı.");
            targets.Add(target.Id, target);
        }
        if (targets.Count == 0) throw new InvalidOperationException("En az bir açık firma hedefi gerekli.");
    }

    public SubscriptionTarget? Find(string id) => targets.GetValueOrDefault(id);

    internal void MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/targets").RequireAuthorization("Operator").RequireRateLimiting("ledger");
        group.MapGet("/", async (HttpContext c, TimeProvider clock, int page = 1, string? search = null, string? includeSummary = null, string access = "all") =>
        {
            var summaries = false;
            if (includeSummary is not null && !bool.TryParse(includeSummary, out summaries) || page is < 1 or > 1000000 || search?.Length > 160 || access is not ("all" or "open" or "restricted" or "unavailable") ||
                !summaries && access != "all" || c.Request.Query.Keys.Any(k => k is not ("page" or "search" or "includeSummary" or "access")))
                return Results.Problem(statusCode: 400, title: "Firma aramasını ve sayfa bilgisini kontrol edin.");
            var found = targets.Values.Where(t => string.IsNullOrWhiteSpace(search) || CultureInfo.GetCultureInfo("tr-TR").CompareInfo.IndexOf(t.Name, search, CompareOptions.IgnoreCase) >= 0 || t.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
            var selected = found.Skip((page - 1) * 12).Take(12).ToArray();
            if (!summaries) return Results.Ok(new { page, total = found.Length, items = selected.Select(t => new { id = t.Id, name = t.Name }) });
            var checkedAt = clock.GetUtcNow();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(checkedAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var items = await Task.WhenAll(selected.Select(async target =>
            {
                var entered = false;
                try
                {
                    await summarySlots.WaitAsync(deadline.Token); entered = true;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await using var db = await target.OpenAsync(timeout.Token);
                    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, timeout.Token);
                    await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", timeout.Token);
                    var summary = await SubscriptionSummary.ReadAsync(db, today, timeout.Token);
                    await transaction.CommitAsync(timeout.Token);
                    return new TargetSummary(target.Id, target.Name, "available", summary);
                }
                catch (Exception e) when (e is NpgsqlException or InvalidOperationException or TimeoutException || e is OperationCanceledException && !c.RequestAborted.IsCancellationRequested)
                { return new TargetSummary(target.Id, target.Name, "unavailable", null); }
                finally { if (entered) summarySlots.Release(); }
            }));
            return Results.Ok(new
            {
                page,
                pageSize = 12,
                total = found.Length,
                checkedAt,
                asOf = today,
                filterScope = "page",
                items = items.Where(item => access == "all" || access == "unavailable" && item.Status == "unavailable" ||
                    access == "restricted" && item.Summary?.RestrictionsApplied == true || access == "open" && item.Summary?.RestrictionsApplied == false)
            });
        });
        group.MapGet("/{targetId}", (string targetId) => Find(targetId) is { } target
            ? Results.Ok(new { id = target.Id, name = target.Name }) : Results.NotFound());
    }

    private sealed record TargetSummary(string Id, string Name, string Status, SubscriptionSummary? Summary);
}
