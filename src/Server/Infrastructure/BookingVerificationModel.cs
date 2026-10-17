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
    }
}
