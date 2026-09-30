namespace Server.Infrastructure;

public sealed class StaffInvitation
{
    public Guid Id { get; set; }
    public required string InstanceId { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string TokenHash { get; set; }
    public Guid IssuedById { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class StaffInvitationAudit
{
    public Guid Id { get; set; }
    public Guid InvitationId { get; set; }
    public Guid ActorId { get; set; }
    public required string Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
