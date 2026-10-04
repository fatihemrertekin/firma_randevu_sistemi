namespace Server.Infrastructure;

// Kaldırılan logo özelliğinin geçmiş işlem kayıtları yalnız okunmak üzere korunur.
public sealed class BusinessLogoAudit
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid LogoVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
