namespace Server.Infrastructure;

public sealed class BookingSmsBudgetPolicy
{
    public Guid Id { get; set; }
    public required string InstanceId { get; set; }
    public required string KeyId { get; set; }
    public decimal UnitPriceTry { get; set; }
    public int DailyCalls { get; set; }
    public int MonthlyCalls { get; set; }
    public int TotalCalls { get; set; }
    public decimal DailyTry { get; set; }
    public decimal MonthlyTry { get; set; }
    public decimal TotalTry { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class BookingSmsAttempt
{
    public Guid JobId { get; set; }
    public int Number { get; set; }
    public Guid PolicyId { get; set; }
    public decimal ReservedTry { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public Guid? LeaseId { get; set; }
    public required string Outcome { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset RetainUntil { get; set; }
}
