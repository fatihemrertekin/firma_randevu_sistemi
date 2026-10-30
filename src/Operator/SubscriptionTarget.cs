using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Infrastructure;

namespace SubscriptionOperator;

public sealed class SubscriptionTarget
{
    public string Id { get; }
    public string Name { get; }
    private readonly string connectionString;

    public SubscriptionTarget(IConfiguration config) : this(config["SubscriptionTarget:InstanceId"], config["SubscriptionTarget:Name"],
        config.GetConnectionString("SubscriptionTarget"), config.GetConnectionString("OperatorIdentity"))
    { }

    internal SubscriptionTarget(string? id, string? name, string? connection, string? identityConnection, bool requireTls = false)
    {
        Id = id ?? "";
        Name = name ?? "";
        NpgsqlConnectionStringBuilder target, identity;
        try { target = new(connection); identity = new(identityConnection); }
        catch (ArgumentException) { throw new InvalidOperationException("Firma bağlantı yapılandırması geçersiz; sunucu ayarlarını kontrol edin."); }
        if (!Regex.IsMatch(Id, "^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.CultureInvariant) || string.IsNullOrWhiteSpace(Name) || Name.Length > 160 ||
            string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.Database) || string.IsNullOrWhiteSpace(target.Username) ||
            string.Equals(target.Host, identity.Host, StringComparison.OrdinalIgnoreCase) && target.Port == identity.Port && target.Database == identity.Database ||
            requireTls && target.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Ayrı kimlik DB'si, açık firma hedefi ve çok firma bağlantısında doğrulanmış TLS gerekli.");
        target.Timeout = 5; target.CommandTimeout = 15; target.MaxPoolSize = 8; target.IncludeErrorDetail = false;
        if (requireTls) target["GSS Encryption Mode"] = "Disable";
        connectionString = target.ConnectionString;
    }

    internal string Destination
    {
        get
        {
            var target = new NpgsqlConnectionStringBuilder(connectionString);
            return $"{target.Host?.ToUpperInvariant()}:{target.Port}/{target.Database}";
        }
    }

    public async Task<AppDbContext> OpenAsync(CancellationToken token)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        try
        {
            await db.Database.OpenConnectionAsync(token);
            var safeRole = await db.Database.SqlQueryRaw<bool>("SELECT current_user = 'subscription_operator' AND NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls) AND NOT EXISTS (SELECT 1 FROM pg_auth_members WHERE member=pg_roles.oid) AS \"Value\" FROM pg_roles WHERE rolname=current_user").SingleAsync(token);
            if (!safeRole) throw new InvalidOperationException("Firma yazma rolü izin verilen dar rol değil.");
            var actual = await db.Database.SqlQueryRaw<string>("SELECT current_setting('app.subscription_instance', true) AS \"Value\"").SingleOrDefaultAsync(token);
            if (actual != Id) throw new InvalidOperationException("Firma hedefi doğrulanamadı. Hiçbir kayıt yazılmadı.");
            return db;
        }
        catch { await db.DisposeAsync(); throw; }
    }
}
