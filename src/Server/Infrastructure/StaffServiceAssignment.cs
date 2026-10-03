namespace Server.Infrastructure;

public sealed class StaffServiceAssignment
{
    public Guid StaffMemberId { get; set; }
    public Guid ServiceDefinitionId { get; set; }
}

public sealed class StaffServiceAssignmentAudit
{
    public Guid Id { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ServiceDefinitionId { get; set; }
    public Guid ActorId { get; set; }
    public required string Kind { get; set; }
    public Guid MemberVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
