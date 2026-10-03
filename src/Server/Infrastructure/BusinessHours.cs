namespace Server.Infrastructure;

public sealed class BusinessHoursSchedule
{
    public int Id { get; set; }
    public bool IsConfigured { get; set; }
    public Guid Version { get; set; }
}

public sealed class BusinessOpeningDay
{
    public int ScheduleId { get; set; }
    // Pazartesi 0, Pazar 6; saatler Europe/Istanbul haftalık yerel duvar saatidir.
    public int Day { get; set; }
    public bool IsClosed { get; set; }
    public int? OpensAtMinute { get; set; }
    public int? ClosesAtMinute { get; set; }
}

public sealed class BusinessHoursAudit
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid ScheduleVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
