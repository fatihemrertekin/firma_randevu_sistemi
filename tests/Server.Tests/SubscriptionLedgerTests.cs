using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.IdentityTestEnvironment;

namespace Server.Tests;

public sealed class SubscriptionLedgerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static AppDbContext Context(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    private static async Task Error(AppDbContext db, string state, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(action);
        Assert.Equal(state, Assert.IsType<PostgresException>(error.InnerException).SqlState); db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ImmutablePaymentReversalAndDatabaseLimitsProtectHistoricalMoney()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        await using var db = Context(database.GetConnectionString()); await db.Database.MigrateAsync(Token);
        var october = SubscriptionData.Period(new(2026, 10, 1)); var november = SubscriptionData.Period(new(2026, 11, 1));
        november.AmountTry = 2000m;
        db.SubscriptionPeriods.AddRange(october, november); await db.SaveChangesAsync(Token);
        var initialVersion = october.Version;
        var original = SubscriptionData.Payment(october.Id, 1000m, "SYNTHETIC-REF-01"); db.SubscriptionPayments.Add(original); await db.SaveChangesAsync(Token);
        await db.Entry(october).ReloadAsync(Token); Assert.NotEqual(initialVersion, october.Version);
        db.SubscriptionPayments.Add(SubscriptionData.Replacement(original, november.Id, 600m)); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        db.SubscriptionPayments.Add(SubscriptionData.Payment(october.Id, .01m, "SYNTHETIC-OVERPAY")); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        var wrong = SubscriptionData.Reversal(original); wrong.AmountTry = -999m; db.SubscriptionPayments.Add(wrong); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        wrong = SubscriptionData.Reversal(original); wrong.PeriodId = november.Id; db.SubscriptionPayments.Add(wrong); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        db.SubscriptionPayments.Add(SubscriptionData.Reversal(original)); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Reversal(original));
        await Error(db, "23514", () => db.SaveChangesAsync(Token));
        db.SubscriptionPayments.Add(SubscriptionData.Payment(october.Id, 1000m, "SYNTHETIC-REF-01")); await Error(db, "23505", () => db.SaveChangesAsync(Token));
        var corrected = SubscriptionData.Payment(october.Id, 1000m, "SYNTHETIC-CORRECTED"); corrected.RequestKey = original.RequestKey;
        db.SubscriptionPayments.Add(corrected); await Error(db, "23505", () => db.SaveChangesAsync(Token));
        var wrongReference = SubscriptionData.Replacement(original, november.Id, 600m); wrongReference.Reference = "WRONG-BANK-REFERENCE";
        db.SubscriptionPayments.Add(wrongReference); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        corrected = SubscriptionData.Replacement(original, november.Id, 600m); db.SubscriptionPayments.Add(corrected); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Replacement(original, november.Id, 600m)); await Error(db, "23505", () => db.SaveChangesAsync(Token));
        db.SubscriptionPayments.Add(SubscriptionData.Reversal(corrected)); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Replacement(corrected, november.Id, 700m)); await db.SaveChangesAsync(Token);
        var before = await SubscriptionData.Snapshot(db);
        var stored = await db.SubscriptionPayments.SingleAsync(p => p.Id == original.Id, Token); stored.AmountTry = 2m;
        await Error(db, "23514", () => db.SaveChangesAsync(Token));
        stored = await db.SubscriptionPayments.SingleAsync(p => p.Id == original.Id, Token); db.Remove(stored);
        await Error(db, "23514", () => db.SaveChangesAsync(Token));
        var period = await db.SubscriptionPeriods.SingleAsync(p => p.Id == october.Id, Token); period.AmountTry = 2000m;
        await Error(db, "23514", () => db.SaveChangesAsync(Token));
        Assert.Equal(before, await SubscriptionData.Snapshot(db)); Assert.Equal(0m, await db.SubscriptionPayments.Where(p => p.PeriodId == october.Id).SumAsync(p => p.AmountTry, Token));
        Assert.Equal(700m, await db.SubscriptionPayments.Where(p => p.PeriodId == november.Id).SumAsync(p => p.AmountTry, Token));
        Assert.Equal(5, await db.SubscriptionPayments.CountAsync(Token));
        db.SubscriptionPeriods.Add(SubscriptionData.Period(new(2026, 10, 20))); await Error(db, "23P01", () => db.SaveChangesAsync(Token));
        var invalid = SubscriptionData.Period(new(2027, 2, 1)); invalid.EndsOn = new(2027, 2, 28);
        db.SubscriptionPeriods.Add(invalid); await Error(db, "23514", () => db.SaveChangesAsync(Token));
        db.SubscriptionPeriods.Add(SubscriptionData.Period(new(2028, 2, 29))); await db.SaveChangesAsync(Token);
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.RepeatableRead)]
    public async Task ConcurrentPaymentsCannotMultiplyThePeriodBalance(IsolationLevel isolation)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        await using var db = Context(database.GetConnectionString()); await db.Database.MigrateAsync(Token);
        var period = SubscriptionData.Period(new(2026, 10, 1)); db.Add(period); await db.SaveChangesAsync(Token);
        var arrived = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Save(string reference)
        {
            await using var writer = Context(database.GetConnectionString());
            await using var transaction = await writer.Database.BeginTransactionAsync(isolation, Token);
            await writer.SubscriptionPayments.CountAsync(Token);
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            writer.Add(SubscriptionData.Payment(period.Id, 1000m, reference));
            try { await writer.SaveChangesAsync(Token); await transaction.CommitAsync(Token); return "committed"; }
            catch (Exception error) when (error.GetBaseException() is PostgresException postgres && postgres.SqlState is "23514" or "40001") { return postgres.SqlState; }
        }
        var results = await Task.WhenAll(Save("RACE-A"), Save("RACE-B"));
        Assert.Single(results, result => result == "committed");
        Assert.Contains(results, result => result is "23514" or "40001");
        Assert.Equal(1, await db.SubscriptionPayments.CountAsync(Token)); Assert.Equal(1000m, await db.SubscriptionPayments.SumAsync(p => p.AmountTry, Token));
    }

    [Fact]
    public async Task NewSchemaPreservesExistingRowsAndDownRefusesToDeleteRecordedSubscriptions()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        await using var db = Context(database.GetConnectionString());
        var migrator = db.GetService<IMigrator>(); const string previous = "20261022235641_AppointmentReminders";
        await migrator.MigrateAsync(previous, Token);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"BusinessProfiles\" SET \"Name\" = 'Synthetic preserved business'", Token);
        var before = await SubscriptionData.Snapshot(db); Assert.Equal(54, before.Count);
        await migrator.MigrateAsync(cancellationToken: Token); var upgraded = await SubscriptionData.Snapshot(db); Assert.Equal(56, upgraded.Count);
        foreach (var table in before.Keys.Where(name => name != "__EFMigrationsHistory")) Assert.Equal(before[table], upgraded[table]);
        Assert.Empty(await db.SubscriptionPeriods.ToArrayAsync(Token));
        await migrator.MigrateAsync(previous, Token); Assert.Equal(before, await SubscriptionData.Snapshot(db));
        await migrator.MigrateAsync(cancellationToken: Token);
        db.Add(SubscriptionData.Period(new(2026, 10, 1))); await db.SaveChangesAsync(Token);
        var recorded = await SubscriptionData.Snapshot(db);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(previous, Token));
        Assert.Equal("P0001", failure.SqlState); Assert.Equal(recorded, await SubscriptionData.Snapshot(db));
    }

    [Fact]
    public async Task ActualMigrationGrantsMakeTheApplicationRoleReadOnlyForBothTables()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        await using var db = Context(database.GetConnectionString()); await db.Database.MigrateAsync(Token);
        var period = SubscriptionData.Period(new(2026, 10, 1)); db.Add(period); await db.SaveChangesAsync(Token);
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "FirmaRandevu.slnx"))) root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository root missing");
        var grants = string.Join('\n', (await File.ReadAllLinesAsync(Path.Combine(root, "deploy/Apply-LocalMigration.ps1"), Token)).Where(line => line.Contains("\"SubscriptionPeriods\", \"SubscriptionPayments\"", StringComparison.Ordinal)));
        Assert.Contains("GRANT SELECT", grants); Assert.Contains("REVOKE INSERT, UPDATE, DELETE, TRUNCATE", grants);
        await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user NOLOGIN", Token);
        await db.Database.ExecuteSqlRawAsync(grants, Token);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token);
        await using (var role = new NpgsqlCommand("SET ROLE app_user", connection)) await role.ExecuteNonQueryAsync(Token);
        foreach (var table in new[] { "SubscriptionPeriods", "SubscriptionPayments" })
        {
            await using var read = new NpgsqlCommand("SELECT COUNT(*) FROM \"" + table + "\"", connection); await read.ExecuteScalarAsync(Token);
            foreach (var verb in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
            {
                await using var privilege = new NpgsqlCommand("SELECT has_table_privilege(current_user, @table, @verb)", connection);
                privilege.Parameters.AddWithValue("table", "\"" + table + "\""); privilege.Parameters.AddWithValue("verb", verb);
                Assert.False((bool)(await privilege.ExecuteScalarAsync(Token))!);
            }
        }
        await using var denied = new NpgsqlCommand("DELETE FROM \"SubscriptionPeriods\"", connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", error.SqlState);
    }
}
