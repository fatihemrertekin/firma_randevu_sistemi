using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure;

internal static class BookingVerificationModel
{
    internal static void Configure(ModelBuilder builder)
    {
        var challenge = builder.Entity<BookingVerification>();
        challenge.HasIndex(x => x.RequestKey).IsUnique();
        challenge.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        challenge.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("\"AppointmentId\" IS NOT NULL");
        foreach (var name in new[] { "RequestHash", "SecretHash", "PhoneHash", "IpHash", "CodeHash" })
            challenge.Property<string>(name).HasMaxLength(64);
        challenge.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingVerification_Consumption", "(\"AppointmentId\" IS NULL AND \"ConsumedAt\" IS NULL AND \"ReplayUntil\" IS NULL) OR (\"AppointmentId\" IS NOT NULL AND \"ConsumedAt\" IS NOT NULL AND isfinite(\"ConsumedAt\") AND \"ReplayUntil\" IS NOT NULL AND \"ReplayUntil\" = \"ConsumedAt\" + INTERVAL '24 hours' AND \"VerifiedAt\" IS NOT NULL AND \"VerifiedUntil\" IS NOT NULL AND \"ConsumedAt\" >= \"VerifiedAt\" AND \"ConsumedAt\" < \"VerifiedUntil\")");
            t.HasCheckConstraint("CK_BookingVerification_Attempts", "\"FailedAttempts\" BETWEEN 0 AND 5 AND \"CodeVersion\" BETWEEN 1 AND 3 AND cardinality(\"UsedCodeHashes\") BETWEEN 0 AND 3");
            t.HasCheckConstraint("CK_BookingVerification_Hashes", "\"RequestHash\" ~ '^[0-9A-F]{64}$' AND \"SecretHash\" ~ '^[0-9A-F]{64}$' AND \"PhoneHash\" ~ '^[0-9A-F]{64}$' AND \"IpHash\" ~ '^[0-9A-F]{64}$' AND (\"CodeHash\" IS NULL OR \"CodeHash\" ~ '^[0-9A-F]{64}$')");
            t.HasCheckConstraint("CK_BookingVerification_Times", "\"ExpiresAt\" > \"CreatedAt\" AND \"ResendAt\" > \"CreatedAt\" AND ((\"VerifiedAt\" IS NULL AND \"VerifiedUntil\" IS NULL) OR (\"VerifiedAt\" IS NOT NULL AND \"VerifiedUntil\" > \"VerifiedAt\" AND \"CodeHash\" IS NULL))");
        });
        challenge.HasIndex(x => x.UpdatedAt);
        var job = builder.Entity<BookingSmsJob>();
        job.HasOne<BookingVerification>().WithMany().HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        job.HasIndex(x => new { x.ChallengeId, x.CodeVersion }).IsUnique();
        job.HasIndex(x => new { x.ChallengeId, x.ResendKey }).IsUnique();
        job.HasIndex(x => new { x.Status, x.NextAttemptAt });
        job.Property(x => x.Status).HasMaxLength(20);
        job.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingSmsJob_State", "\"Status\" IN ('Pending','Processing','Accepted','Failed','Unknown','Cancelled','Expired') AND \"Attempts\" BETWEEN 0 AND 3 AND \"CodeVersion\" >= 1");
            t.HasCheckConstraint("CK_BookingSmsJob_Lease", "(\"Status\" = 'Processing' AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
        });
        var quota = builder.Entity<BookingVerificationQuotaEvent>();
        quota.Property(x => x.Kind).HasMaxLength(10);
        quota.Property(x => x.KeyId).HasMaxLength(64);
        quota.Property(x => x.PhoneHash).HasMaxLength(64);
        quota.Property(x => x.IpHash).HasMaxLength(64);
        quota.HasIndex(x => new { x.Kind, x.PhoneHash, x.OccurredAt });
        quota.HasIndex(x => new { x.Kind, x.IpHash, x.OccurredAt });
        quota.HasIndex(x => new { x.Kind, x.OccurredAt });
        quota.ToTable(t => t.HasCheckConstraint("CK_BookingVerificationQuota_Kind", "\"KeyId\" ~ '^[0-9A-F]{64}$' AND \"Kind\" IN ('Send','Wrong') AND \"RetainUntil\" > \"OccurredAt\""));
        var period = builder.Entity<BookingSmsPeriod>();
        period.HasKey(x => x.Key);
        period.Property(x => x.Key).HasMaxLength(16);
        period.ToTable(t => t.HasCheckConstraint("CK_BookingSmsPeriod_Attempts", "\"Attempts\" BETWEEN 0 AND 2000"));
        var policy = builder.Entity<BookingSmsBudgetPolicy>();
        policy.Property(x => x.InstanceId).HasMaxLength(100);
        policy.Property(x => x.KeyId).HasMaxLength(64);
        foreach (var name in new[] { "UnitPriceTry", "DailyTry", "MonthlyTry", "TotalTry" }) policy.Property<decimal>(name).HasPrecision(10, 2);
        policy.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingSmsBudgetPolicy_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(\"InstanceId\") BETWEEN 1 AND 100 AND \"KeyId\" ~ '^[0-9A-F]{64}$'");
            t.HasCheckConstraint("CK_BookingSmsBudgetPolicy_Limits", "\"UnitPriceTry\" > 0 AND \"UnitPriceTry\" <= 0.50 AND \"DailyCalls\" BETWEEN 1 AND 20 AND \"MonthlyCalls\" BETWEEN 1 AND 200 AND \"TotalCalls\" BETWEEN 1 AND 200 AND \"DailyTry\" > 0 AND \"DailyTry\" <= 10 AND \"MonthlyTry\" > 0 AND \"MonthlyTry\" <= 100 AND \"TotalTry\" > 0 AND \"TotalTry\" <= 100");
            t.HasCheckConstraint("CK_BookingSmsBudgetPolicy_Times", "isfinite(\"StartsAt\") AND isfinite(\"EndsAt\") AND \"EndsAt\" > \"StartsAt\" AND \"EndsAt\" <= \"StartsAt\" + INTERVAL '31 days' AND (\"RevokedAt\" IS NULL OR isfinite(\"RevokedAt\"))");
        });
        var attempt = builder.Entity<BookingSmsAttempt>();
        attempt.HasKey(x => new { x.JobId, x.Number });
        // İşin 24 saatlik temizliği mali rezervasyonu/sağlayıcı referansını silemez.
        attempt.HasOne<BookingSmsBudgetPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        attempt.Property(x => x.ReservedTry).HasPrecision(10, 2);
        attempt.Property(x => x.Outcome).HasMaxLength(20);
        attempt.Property(x => x.ProviderReference).HasMaxLength(80);
        attempt.HasIndex(x => new { x.PolicyId, x.ReservedAt });
        attempt.HasIndex(x => x.ReservedAt);
        attempt.HasIndex(x => x.RetainUntil);
        attempt.ToTable(t =>
        {
            t.HasCheckConstraint("CK_BookingSmsAttempt_Budget", "\"JobId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Number\" BETWEEN 1 AND 3 AND \"ReservedTry\" > 0 AND \"ReservedTry\" <= 0.50 AND isfinite(\"ReservedAt\") AND isfinite(\"RetainUntil\") AND \"RetainUntil\" >= \"ReservedAt\" + INTERVAL '30 days'");
            t.HasCheckConstraint("CK_BookingSmsAttempt_State", "(\"StartedAt\" IS NULL AND \"LeaseId\" IS NULL AND \"Outcome\" IN ('Reserved','Released') AND \"ProviderReference\" IS NULL) OR (\"StartedAt\" IS NOT NULL AND isfinite(\"StartedAt\") AND \"StartedAt\" >= \"ReservedAt\" AND \"LeaseId\" IS NOT NULL AND \"Outcome\" IN ('Started','Accepted','TemporaryFailure','PermanentFailure','Unknown'))");
            t.HasCheckConstraint("CK_BookingSmsAttempt_Reference", "\"ProviderReference\" IS NULL OR \"ProviderReference\" ~ '^[0-9]{1,80}$'");
        });
    }
}
