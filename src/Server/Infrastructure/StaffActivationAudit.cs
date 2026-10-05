namespace Server.Infrastructure;

public sealed class StaffActivationAudit
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
