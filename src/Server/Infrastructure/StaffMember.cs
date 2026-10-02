namespace Server.Infrastructure;

// Randevu alacak personel tanımıdır; Identity giriş hesabından bağımsızdır.
public sealed class StaffMember
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; }
}

public sealed class StaffMemberAudit
{
    public Guid Id { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ActorId { get; set; }
    public required string Kind { get; set; }
    public Guid MemberVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
