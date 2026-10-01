namespace Server.Infrastructure;

public sealed class BusinessProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public Guid Version { get; set; }
}

public sealed class BusinessProfileAudit
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid ProfileVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
