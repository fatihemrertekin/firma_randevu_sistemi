using Microsoft.EntityFrameworkCore;

namespace Server.Infrastructure;

internal static class AppointmentReminderModel
{
    internal static void Configure(ModelBuilder builder)
    {
        var attempt = builder.Entity<BookingSmsAttempt>();
        attempt.Property(x => x.Purpose).HasMaxLength(8).HasDefaultValue("Otp");
        attempt.ToTable(t => t.HasCheckConstraint("CK_BookingSmsAttempts_Purpose", "\"Purpose\" IN ('Otp','Reminder')"));
        var job = builder.Entity<AppointmentReminderJob>();
        job.HasIndex(x => x.AppointmentId).IsUnique();
        job.HasIndex(x => new { x.Status, x.NextAttemptAt });
        job.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        job.HasOne<AppUser>().WithMany().HasForeignKey(x => x.SuppressedBy).OnDelete(DeleteBehavior.Restrict);
        job.Property(x => x.Status).HasMaxLength(16);
        job.Property(x => x.ProtectedPhone).HasMaxLength(4096);
        job.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AppointmentReminderJobs_State", "\"Status\" IN ('Pending','Processing','Accepted','Unknown','Failed','Cancelled','TooLate','Expired','Budget','Suppressed') AND \"Attempts\" BETWEEN 0 AND 3");
            t.HasCheckConstraint("CK_AppointmentReminderJobs_Time", "isfinite(\"StartsAt\") AND \"DueAt\" = \"StartsAt\" - INTERVAL '3 hours' AND \"Deadline\" = \"DueAt\" + INTERVAL '15 minutes' AND isfinite(\"NextAttemptAt\")");
            t.HasCheckConstraint("CK_AppointmentReminderJobs_Lease", "(\"Status\" = 'Processing' AND \"LeaseId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'Processing' AND \"LeaseId\" IS NULL AND \"LeaseUntil\" IS NULL)");
            t.HasCheckConstraint("CK_AppointmentReminderJobs_Payload", "(\"Status\" IN ('Pending','Processing') AND \"ProtectedPhone\" IS NOT NULL AND \"SuppressedAt\" IS NULL) OR (\"Status\" NOT IN ('Pending','Processing') AND \"ProtectedPhone\" IS NULL)");
            t.HasCheckConstraint("CK_AppointmentReminderJobs_Started", "(\"Attempts\" = 0 AND \"StartedOnceAt\" IS NULL) OR (\"Attempts\" > 0 AND \"StartedOnceAt\" IS NOT NULL)");
        });
    }
}
