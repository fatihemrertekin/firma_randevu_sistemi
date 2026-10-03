namespace Server.Infrastructure;

public sealed class BusinessLogo
{
    public int Id { get; set; }
    public Guid Version { get; set; }
    public byte[]? Png { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

public sealed class BusinessLogoAudit
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid LogoVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
