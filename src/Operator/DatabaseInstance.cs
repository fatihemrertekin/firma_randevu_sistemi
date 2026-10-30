using Microsoft.EntityFrameworkCore;

namespace SubscriptionOperator;

internal static class DatabaseInstance
{
    internal static Task<string?> ReadAsync(DbContext db, string name, CancellationToken token)
    {
        var prefix = name + "=";
        // Oturum/rol/connection Options değeri kalıcı provisioning işaretçisinin yerine geçemez.
        return db.Database.SqlQuery<string>($"SELECT split_part(setting, '=', 2) AS \"Value\" FROM pg_db_role_setting CROSS JOIN LATERAL unnest(setconfig) AS setting WHERE setdatabase = (SELECT oid FROM pg_database WHERE datname = current_database()) AND setrole = 0 AND starts_with(setting, {prefix})")
            .SingleOrDefaultAsync(token);
    }
}
