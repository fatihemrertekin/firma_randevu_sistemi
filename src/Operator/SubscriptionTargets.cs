using System.Globalization;
using System.Net;
using Npgsql;

namespace SubscriptionOperator;

public sealed class SubscriptionTargets
{
    private readonly Dictionary<string, SubscriptionTarget> targets = new(StringComparer.Ordinal);
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
        group.MapGet("/", (HttpContext c, int page = 1, string? search = null) =>
        {
            if (page is < 1 or > 1000000 || search?.Length > 160 || c.Request.Query.Keys.Any(k => k is not ("page" or "search")))
                return Results.Problem(statusCode: 400, title: "Firma aramasını ve sayfa bilgisini kontrol edin.");
            var found = targets.Values.Where(t => string.IsNullOrWhiteSpace(search) || CultureInfo.GetCultureInfo("tr-TR").CompareInfo.IndexOf(t.Name, search, CompareOptions.IgnoreCase) >= 0 || t.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
            return Results.Ok(new { page, total = found.Length, items = found.Skip((page - 1) * 12).Take(12).Select(t => new { id = t.Id, name = t.Name }) });
        });
        group.MapGet("/{targetId}", (string targetId) => Find(targetId) is { } target
            ? Results.Ok(new { id = target.Id, name = target.Name }) : Results.NotFound());
    }
}
