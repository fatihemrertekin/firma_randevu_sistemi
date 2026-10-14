namespace Server.Infrastructure;

public sealed class BusinessDateException
{
    public int ScheduleId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; }
    public int? OpensAtMinute { get; set; }
    public int? ClosesAtMinute { get; set; }
}
public sealed class StaffDateException
{
    public Guid StaffMemberId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsClosed { get; set; }
    public int? OpensAtMinute { get; set; }
    public int? ClosesAtMinute { get; set; }
}
public sealed class BusinessDateBreak
{
    public int ScheduleId { get; set; }
    public DateOnly Date { get; set; }
    public int Position { get; set; }
    public int StartsAtMinute { get; set; }
    public int EndsAtMinute { get; set; }
}
public sealed class StaffDateBreak
{
    public Guid StaffMemberId { get; set; }
    public DateOnly Date { get; set; }
    public int Position { get; set; }
    public int StartsAtMinute { get; set; }
    public int EndsAtMinute { get; set; }
}
