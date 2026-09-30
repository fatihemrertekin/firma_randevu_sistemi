using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure;

public sealed class AppUser : IdentityUser<Guid> { }

public sealed class OwnerMfaRecoveryAudit
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public required string InstanceId { get; set; }
    public required string OperatorReference { get; set; }
    public required string RequestReference { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<OwnerMfaRecoveryAudit> OwnerMfaRecoveryAudits => Set<OwnerMfaRecoveryAudit>();
    public DbSet<OwnerPasswordResetAudit> OwnerPasswordResetAudits => Set<OwnerPasswordResetAudit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var audit = builder.Entity<OwnerMfaRecoveryAudit>();
        audit.Property(entry => entry.InstanceId).HasMaxLength(128);
        audit.Property(entry => entry.OperatorReference).HasMaxLength(64);
        audit.Property(entry => entry.RequestReference).HasMaxLength(64);
        audit.HasIndex(entry => new { entry.InstanceId, entry.RequestReference }).IsUnique();
        var resetAudit = builder.Entity<OwnerPasswordResetAudit>();
        resetAudit.Property(entry => entry.InstanceId).HasMaxLength(128);
        resetAudit.Property(entry => entry.OperatorReference).HasMaxLength(64);
        resetAudit.Property(entry => entry.RequestReference).HasMaxLength(64);
        resetAudit.Property(entry => entry.Kind).HasMaxLength(16);
        resetAudit.HasIndex(entry => new { entry.InstanceId, entry.RequestReference, entry.Kind }).IsUnique();
        resetAudit.HasIndex(entry => new { entry.GrantId, entry.Kind }).IsUnique();
    }
}
