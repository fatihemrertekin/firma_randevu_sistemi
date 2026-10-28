using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure;

public sealed class SubscriptionPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public DateOnly DueOn { get; set; }
    public decimal AmountTry { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid RecordedBy { get; set; }
    public DateTime RecordedAt { get; set; }
}

public sealed class SubscriptionPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PeriodId { get; set; }
    public decimal AmountTry { get; set; }
    public DateOnly PaidOn { get; set; }
    public string? Reference { get; set; }
    public Guid? ReversesPaymentId { get; set; }
    public string? Reason { get; set; }
    public Guid RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public Guid RecordedBy { get; set; }
    public DateTime RecordedAt { get; set; }
}

internal static class SubscriptionSchema
{
    internal static void ConfigureSubscriptions(this ModelBuilder builder)
    {
        var period = builder.Entity<SubscriptionPeriod>();
        period.HasKey(p => p.Id);
        period.Property(p => p.AmountTry).HasPrecision(12, 2);
        period.Property(p => p.Version).IsConcurrencyToken();
        period.HasIndex(p => p.StartsOn).IsUnique();
        period.ToTable("SubscriptionPeriods", table =>
        {
            table.HasCheckConstraint("CK_SubscriptionPeriods_Dates", "\"StartsOn\" >= DATE '0001-01-01' AND \"EndsOn\" <= DATE '9999-12-31' AND \"StartsOn\" < \"EndsOn\" AND \"EndsOn\" = (date_trunc('month', \"StartsOn\"::timestamp) + INTERVAL '1 month')::date AND \"DueOn\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
            table.HasCheckConstraint("CK_SubscriptionPeriods_Amount", "\"AmountTry\" > 0 AND \"AmountTry\" <> 'NaN'::numeric");
            table.HasCheckConstraint("CK_SubscriptionPeriods_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"Version\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RecordedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"RecordedAt\")");
        });
        var payment = builder.Entity<SubscriptionPayment>();
        payment.HasKey(p => p.Id);
        payment.Property(p => p.AmountTry).HasPrecision(12, 2);
        payment.Property(p => p.Reference).HasMaxLength(80);
        payment.Property(p => p.Reason).HasMaxLength(300);
        payment.Property(p => p.RequestHash).HasMaxLength(64);
        payment.HasOne<SubscriptionPeriod>().WithMany().HasForeignKey(p => p.PeriodId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne<SubscriptionPayment>().WithMany().HasForeignKey(p => p.ReversesPaymentId).OnDelete(DeleteBehavior.Restrict);
        payment.HasIndex(p => p.RequestKey).IsUnique();
        payment.HasIndex(p => p.Reference).IsUnique();
        payment.HasIndex(p => p.ReversesPaymentId).IsUnique();
        payment.ToTable("SubscriptionPayments", table =>
        {
            table.HasCheckConstraint("CK_SubscriptionPayments_Amount", "\"AmountTry\" <> 0 AND \"AmountTry\" <> 'NaN'::numeric");
            table.HasCheckConstraint("CK_SubscriptionPayments_Kind", "(\"ReversesPaymentId\" IS NULL AND \"AmountTry\" > 0 AND \"Reference\" IS NOT NULL AND \"Reference\" ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,79}$' AND \"Reason\" IS NULL) OR (\"ReversesPaymentId\" IS NOT NULL AND \"AmountTry\" < 0 AND \"Reference\" IS NULL AND \"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
            table.HasCheckConstraint("CK_SubscriptionPayments_Identity", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestKey\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RecordedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"RequestHash\" ~ '^[0-9A-F]{64}$' AND isfinite(\"RecordedAt\") AND \"PaidOn\" BETWEEN DATE '0001-01-01' AND DATE '9999-12-31'");
        });
    }
}
