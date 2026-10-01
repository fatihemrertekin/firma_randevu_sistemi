namespace Server.Infrastructure;

public sealed class StaffPasswordResetAudit
{
    public Guid Id { get; set; }
    public Guid GrantId { get; set; }
    public Guid StaffId { get; set; }
    public Guid ActorId { get; set; }
    public required string InstanceId { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
