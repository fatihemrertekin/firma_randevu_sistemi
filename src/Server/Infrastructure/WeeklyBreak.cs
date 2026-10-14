namespace Server.Infrastructure;

public sealed class BusinessWeeklyBreak
{
    public int ScheduleId { get; set; }
    public int Day { get; set; }
    public int Position { get; set; }
    public int StartsAtMinute { get; set; }
    public int EndsAtMinute { get; set; }
}

public sealed class StaffWeeklyBreak
{
    public Guid StaffMemberId { get; set; }
    public int Day { get; set; }
    public int Position { get; set; }
    public int StartsAtMinute { get; set; }
    public int EndsAtMinute { get; set; }
}
