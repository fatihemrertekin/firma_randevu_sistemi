namespace Server.Infrastructure;

public sealed class OwnerRecoveryEmail
{
    public Guid OwnerId { get; set; }
    public required string EmailHash { get; set; }
    public required string StampHash { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}
