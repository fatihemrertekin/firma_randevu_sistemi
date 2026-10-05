using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure;

public sealed class AppUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
}

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
    public DbSet<BusinessProfile> BusinessProfiles => Set<BusinessProfile>();
    public DbSet<BusinessProfileAudit> BusinessProfileAudits => Set<BusinessProfileAudit>();
    public DbSet<OwnerRecoveryEmail> OwnerRecoveryEmails => Set<OwnerRecoveryEmail>();
    public DbSet<OwnerSelfServiceReset> OwnerSelfServiceResets => Set<OwnerSelfServiceReset>();
    public DbSet<IdentityEmailQuota> IdentityEmailQuotas => Set<IdentityEmailQuota>();
    public DbSet<StaffDeactivationAudit> StaffDeactivationAudits => Set<StaffDeactivationAudit>();
    public DbSet<StaffActivationAudit> StaffActivationAudits => Set<StaffActivationAudit>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffMemberAudit> StaffMemberAudits => Set<StaffMemberAudit>();
    public DbSet<ServiceDefinition> ServiceDefinitions => Set<ServiceDefinition>();
    public DbSet<ServiceDefinitionAudit> ServiceDefinitionAudits => Set<ServiceDefinitionAudit>();
    public DbSet<StaffServiceAssignment> StaffServiceAssignments => Set<StaffServiceAssignment>();
    public DbSet<StaffServiceAssignmentAudit> StaffServiceAssignmentAudits => Set<StaffServiceAssignmentAudit>();
    public DbSet<BusinessHoursSchedule> BusinessHoursSchedules => Set<BusinessHoursSchedule>();
    public DbSet<BusinessOpeningDay> BusinessOpeningDays => Set<BusinessOpeningDay>();
    public DbSet<BusinessHoursAudit> BusinessHoursAudits => Set<BusinessHoursAudit>();
    public DbSet<StaffWorkingDay> StaffWorkingDays => Set<StaffWorkingDay>();
    public DbSet<StaffHoursAudit> StaffHoursAudits => Set<StaffHoursAudit>();
    public DbSet<BusinessLogoAudit> BusinessLogoAudits => Set<BusinessLogoAudit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Tek zaman çizelgesinin her kaynağında anahtarlı sayfalama: eski kayıt/veri değişmez.
        builder.Entity<BusinessProfileAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<BusinessLogoAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<BusinessHoursAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffMemberAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<ServiceDefinitionAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffServiceAssignmentAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffHoursAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffInvitationAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffPasswordResetAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffDeactivationAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<StaffActivationAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<OwnerPasswordResetAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        builder.Entity<OwnerMfaRecoveryAudit>().HasIndex(entry => new { entry.OccurredAt, entry.Id }).IsDescending(true, true);
        var logoAudit = builder.Entity<BusinessLogoAudit>();
        logoAudit.HasIndex(entry => entry.LogoVersion).IsUnique();
        logoAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AppUser>().Property(user => user.IsActive).HasDefaultValue(true);
        var workingDay = builder.Entity<StaffWorkingDay>();
        workingDay.HasKey(entry => new { entry.StaffMemberId, entry.Day });
        workingDay.HasOne<StaffMember>().WithMany().HasForeignKey(entry => entry.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        workingDay.ToTable(table =>
        {
            table.HasCheckConstraint("CK_StaffWorkingDays_Day", "\"Day\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_StaffWorkingDays_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
        });
        var staffHoursAudit = builder.Entity<StaffHoursAudit>();
        staffHoursAudit.HasIndex(entry => new { entry.StaffMemberId, entry.MemberVersion }).IsUnique();
        staffHoursAudit.HasOne<StaffMember>().WithMany().HasForeignKey(entry => entry.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        staffHoursAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var hours = builder.Entity<BusinessHoursSchedule>();
        hours.Property(entry => entry.Id).ValueGeneratedNever();
        hours.Property(entry => entry.Version).IsConcurrencyToken();
        hours.ToTable(table => table.HasCheckConstraint("CK_BusinessHoursSchedules_Singleton", "\"Id\" = 1"));
        hours.HasData(new BusinessHoursSchedule { Id = 1, Version = Guid.Parse("d317d899-8208-41f1-9b8e-c6fbde437cde") });
        var openingDay = builder.Entity<BusinessOpeningDay>();
        openingDay.HasKey(entry => new { entry.ScheduleId, entry.Day });
        openingDay.HasOne<BusinessHoursSchedule>().WithMany().HasForeignKey(entry => entry.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        openingDay.ToTable(table =>
        {
            table.HasCheckConstraint("CK_BusinessOpeningDays_Day", "\"Day\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_BusinessOpeningDays_Hours", "(\"IsClosed\" AND \"OpensAtMinute\" IS NULL AND \"ClosesAtMinute\" IS NULL) OR (NOT \"IsClosed\" AND \"OpensAtMinute\" IS NOT NULL AND \"ClosesAtMinute\" IS NOT NULL AND \"OpensAtMinute\" BETWEEN 0 AND 1439 AND \"ClosesAtMinute\" BETWEEN 0 AND 1439 AND \"OpensAtMinute\" < \"ClosesAtMinute\")");
        });
        var hoursAudit = builder.Entity<BusinessHoursAudit>();
        hoursAudit.HasIndex(entry => entry.ScheduleVersion).IsUnique();
        hoursAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var assignment = builder.Entity<StaffServiceAssignment>();
        assignment.HasKey(entry => new { entry.StaffMemberId, entry.ServiceDefinitionId });
        assignment.HasOne<StaffMember>().WithMany().HasForeignKey(entry => entry.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<ServiceDefinition>().WithMany().HasForeignKey(entry => entry.ServiceDefinitionId).OnDelete(DeleteBehavior.Restrict);
        var assignmentAudit = builder.Entity<StaffServiceAssignmentAudit>();
        assignmentAudit.Property(entry => entry.Kind).HasMaxLength(16);
        assignmentAudit.ToTable(table => table.HasCheckConstraint("CK_StaffServiceAssignmentAudits_Kind", "\"Kind\" IN ('Assigned','Unassigned')"));
        assignmentAudit.HasIndex(entry => new { entry.StaffMemberId, entry.ServiceDefinitionId, entry.MemberVersion }).IsUnique();
        assignmentAudit.HasOne<StaffMember>().WithMany().HasForeignKey(entry => entry.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        assignmentAudit.HasOne<ServiceDefinition>().WithMany().HasForeignKey(entry => entry.ServiceDefinitionId).OnDelete(DeleteBehavior.Restrict);
        assignmentAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var service = builder.Entity<ServiceDefinition>();
        service.Property(entry => entry.Name).HasMaxLength(100);
        service.Property(entry => entry.Currency).HasMaxLength(3);
        service.Property(entry => entry.Price).HasPrecision(8, 2);
        service.Property(entry => entry.Version).IsConcurrencyToken();
        service.Property(entry => entry.IsDeleted).HasDefaultValue(false);
        service.HasIndex(entry => new { entry.Name, entry.Id });
        service.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ServiceDefinitions_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_ServiceDefinitions_Duration", "\"DurationMinutes\" BETWEEN 1 AND 1440");
            table.HasCheckConstraint("CK_ServiceDefinitions_Price", "\"Price\" BETWEEN 0 AND 999999.99");
            table.HasCheckConstraint("CK_ServiceDefinitions_Currency", "\"Currency\" = 'TRY'");
            table.HasCheckConstraint("CK_ServiceDefinitions_DeletedInactive", "NOT \"IsDeleted\" OR NOT \"IsActive\"");
        });
        var serviceAudit = builder.Entity<ServiceDefinitionAudit>();
        serviceAudit.Property(entry => entry.Kind).HasMaxLength(16);
        serviceAudit.ToTable(table => table.HasCheckConstraint("CK_ServiceDefinitionAudits_Kind", "\"Kind\" IN ('Created','Updated','Activated','Deactivated','Deleted')"));
        serviceAudit.HasIndex(entry => new { entry.ServiceDefinitionId, entry.ServiceVersion }).IsUnique();
        serviceAudit.HasOne<ServiceDefinition>().WithMany().HasForeignKey(entry => entry.ServiceDefinitionId).OnDelete(DeleteBehavior.Restrict);
        serviceAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var member = builder.Entity<StaffMember>();
        member.Property(entry => entry.Name).HasMaxLength(100);
        member.Property(entry => entry.Version).IsConcurrencyToken();
        member.Property(entry => entry.IsDeleted).HasDefaultValue(false);
        member.ToTable(table =>
        {
            table.HasCheckConstraint("CK_StaffMembers_Name", "length(btrim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_StaffMembers_DeletedInactive", "NOT \"IsDeleted\" OR NOT \"IsActive\"");
        });
        member.HasIndex(entry => new { entry.Name, entry.Id });
        var memberAudit = builder.Entity<StaffMemberAudit>();
        memberAudit.Property(entry => entry.Kind).HasMaxLength(16);
        memberAudit.ToTable(table => table.HasCheckConstraint("CK_StaffMemberAudits_Kind",
            "\"Kind\" IN ('Created','Renamed','Activated','Deactivated','Deleted')"));
        memberAudit.HasIndex(entry => new { entry.StaffMemberId, entry.MemberVersion }).IsUnique();
        memberAudit.HasOne<StaffMember>().WithMany().HasForeignKey(entry => entry.StaffMemberId).OnDelete(DeleteBehavior.Restrict);
        memberAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var deactivation = builder.Entity<StaffDeactivationAudit>();
        deactivation.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.StaffId).OnDelete(DeleteBehavior.Restrict);
        deactivation.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        var activation = builder.Entity<StaffActivationAudit>();
        activation.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.StaffId).OnDelete(DeleteBehavior.Restrict);
        activation.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<IdentityEmailQuota>().HasKey(entry => entry.Day);
        var reset = builder.Entity<OwnerSelfServiceReset>();
        reset.HasKey(entry => entry.OwnerId);
        reset.HasIndex(entry => entry.GrantId).IsUnique();
        reset.HasIndex(entry => new { entry.Status, entry.NextAttemptAt });
        reset.Property(entry => entry.EmailHash).HasMaxLength(64);
        reset.Property(entry => entry.StampHash).HasMaxLength(64);
        reset.Property(entry => entry.ProtectedPayload).HasMaxLength(16384);
        reset.Property(entry => entry.Status).HasMaxLength(16);
        reset.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.OwnerId).OnDelete(DeleteBehavior.Restrict);
        var recoveryEmail = builder.Entity<OwnerRecoveryEmail>();
        recoveryEmail.HasKey(entry => entry.OwnerId);
        recoveryEmail.Property(entry => entry.EmailHash).HasMaxLength(64);
        recoveryEmail.Property(entry => entry.StampHash).HasMaxLength(64);
        recoveryEmail.Property(entry => entry.TokenHash).HasMaxLength(64);
        recoveryEmail.HasIndex(entry => entry.TokenHash).IsUnique();
        recoveryEmail.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.OwnerId).OnDelete(DeleteBehavior.Restrict);
        var profile = builder.Entity<BusinessProfile>();
        profile.ToTable(table => table.HasCheckConstraint("CK_BusinessProfiles_Singleton", "\"Id\" = 1"));
        profile.Property(entry => entry.Id).ValueGeneratedNever();
        profile.Property(entry => entry.Name).HasMaxLength(150);
        profile.Property(entry => entry.Phone).HasMaxLength(13);
        profile.Property(entry => entry.Email).HasMaxLength(254);
        profile.Property(entry => entry.Address).HasMaxLength(500);
        profile.Property(entry => entry.Version).IsConcurrencyToken();
        profile.HasData(new BusinessProfile { Id = 1, Name = "", Version = Guid.Parse("21dd6c8a-8755-4111-a7f1-a527f9b1c6b3") });
        var profileAudit = builder.Entity<BusinessProfileAudit>();
        profileAudit.HasOne<AppUser>().WithMany().HasForeignKey(entry => entry.ActorId).OnDelete(DeleteBehavior.Restrict);
        profileAudit.HasIndex(entry => entry.ProfileVersion).IsUnique();
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
