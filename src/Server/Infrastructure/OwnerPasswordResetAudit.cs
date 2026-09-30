namespace Server.Infrastructure;

public sealed class OwnerPasswordResetAudit
{
    public Guid Id { get; set; }
    public Guid GrantId { get; set; }
    public Guid OwnerId { get; set; }
    public required string InstanceId { get; set; }
    public required string OperatorReference { get; set; }
    public required string RequestReference { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
