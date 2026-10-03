namespace Server.Infrastructure;

public sealed class StaffWorkingDay
{
    public Guid StaffMemberId { get; set; }
    // Pazartesi 0, Pazar 6; Europe/Istanbul yerel haftalık saatleri.
    public int Day { get; set; }
    public bool IsClosed { get; set; }
    public int? OpensAtMinute { get; set; }
    public int? ClosesAtMinute { get; set; }
}

public sealed class StaffHoursAudit
{
    public Guid Id { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ActorId { get; set; }
    public Guid MemberVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
