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
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<StaffInvitationAudit> StaffInvitationAudits => Set<StaffInvitationAudit>();
    public DbSet<StaffPasswordResetAudit> StaffPasswordResetAudits => Set<StaffPasswordResetAudit>();

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
        builder.Entity<IdentityRole<Guid>>().HasData(new IdentityRole<Guid>
        {
            Id = Guid.Parse("6b857aae-086b-4522-b8a7-d5b104b660de"),
            Name = "Staff",
            NormalizedName = "STAFF",
            ConcurrencyStamp = "6b857aae-086b-4522-b8a7-d5b104b660de"
        });
        var invitation = builder.Entity<StaffInvitation>();
        invitation.Property(entry => entry.InstanceId).HasMaxLength(128);
        invitation.Property(entry => entry.Email).HasMaxLength(256);
        invitation.Property(entry => entry.NormalizedEmail).HasMaxLength(256);
        invitation.Property(entry => entry.TokenHash).HasMaxLength(64);
        invitation.HasIndex(entry => new { entry.InstanceId, entry.NormalizedEmail }).IsUnique();
        invitation.HasIndex(entry => new { entry.InstanceId, entry.TokenHash }).IsUnique();
        var invitationAudit = builder.Entity<StaffInvitationAudit>();
        invitationAudit.Property(entry => entry.Kind).HasMaxLength(16);
        invitationAudit.HasOne<StaffInvitation>().WithMany().HasForeignKey(entry => entry.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);
        var staffResetAudit = builder.Entity<StaffPasswordResetAudit>();
        staffResetAudit.Property(entry => entry.InstanceId).HasMaxLength(128);
        staffResetAudit.Property(entry => entry.Kind).HasMaxLength(16);
        staffResetAudit.HasIndex(entry => new { entry.GrantId, entry.Kind }).IsUnique();
        staffResetAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
        staffResetAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
