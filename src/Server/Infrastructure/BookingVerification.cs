namespace Server.Infrastructure;

public sealed class BookingVerification
{
    public Guid Id { get; set; }
    public Guid RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string SecretHash { get; set; }
    public required string PhoneHash { get; set; }
    public required string IpHash { get; set; }
    public string? CodeHash { get; set; }
    public string? ProtectedPhone { get; set; }
    public string[] UsedCodeHashes { get; set; } = [];
    public int CodeVersion { get; set; } = 1;
    public int FailedAttempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResendAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? VerifiedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BookingSmsJob
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? ResendKey { get; set; }
    public int CodeVersion { get; set; }
    public string? ProtectedPayload { get; set; }
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ResendAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}

public sealed class BookingVerificationQuotaEvent
{
    public Guid Id { get; set; }
    public required string KeyId { get; set; }
    public required string Kind { get; set; }
    public required string PhoneHash { get; set; }
    public required string IpHash { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RetainUntil { get; set; }
}

public sealed class BookingSmsPeriod
{
    public required string Key { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}
