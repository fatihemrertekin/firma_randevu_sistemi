namespace Server.Infrastructure;

// One current grant and durable delivery job per Owner. Bearer proof is separately encrypted at rest.
public sealed class OwnerSelfServiceReset
{
    public Guid OwnerId { get; set; }
    public Guid GrantId { get; set; }
    public required string EmailHash { get; set; }
    public required string StampHash { get; set; }
    public string? ProtectedPayload { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required string Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}
