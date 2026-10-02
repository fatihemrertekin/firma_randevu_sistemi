namespace Server.Infrastructure;

public sealed class StaffDeactivationAudit
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
