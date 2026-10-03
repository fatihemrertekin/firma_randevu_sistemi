namespace Server.Infrastructure;

public sealed class ServiceDefinition
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; }
}

public sealed class ServiceDefinitionAudit
{
    public Guid Id { get; set; }
    public Guid ServiceDefinitionId { get; set; }
    public Guid ActorId { get; set; }
    public required string Kind { get; set; }
    public Guid ServiceVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
