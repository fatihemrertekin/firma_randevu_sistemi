namespace Server.Infrastructure;

public sealed class AppointmentReminderJob
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid AppointmentVersion { get; set; }
    public Guid BudgetApprovalId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public required string Status { get; set; }
    public string? ProtectedPhone { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? StartedOnceAt { get; set; }
    public DateTimeOffset? SuppressedAt { get; set; }
    public Guid? SuppressedBy { get; set; }
}
