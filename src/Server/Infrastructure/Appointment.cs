using NpgsqlTypes;

namespace Server.Infrastructure;

public sealed class Appointment
{
    public Guid Id { get; set; }
    public Guid? ActorId { get; set; }
    public string Source { get; set; } = "Owner";
    public Guid? BookingPolicyVersion { get; set; }
    public int? CustomerCancellationLeadMinutes { get; set; }
    public bool? RequiresBusinessApproval { get; set; }
    public Guid RequestKey { get; set; }
    public required string RequestHash { get; set; }
    public required string CustomerName { get; set; }
    public required string CustomerPhone { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid Version { get; set; }
}

public sealed class AppointmentTimeBlock
{
    public Guid AppointmentId { get; set; }
    public Guid StaffMemberId { get; set; }
    public Guid ServiceDefinitionId { get; set; }
    public required string StaffName { get; set; }
    public required string ServiceName { get; set; }
    public int DurationMinutes { get; set; }
    public int BufferBeforeMinutes { get; set; }
    public int BufferAfterMinutes { get; set; }
    public decimal Price { get; set; }
    public required string Currency { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime OccupiedStartsAt { get; set; }
    public DateTime OccupiedEndsAt { get; set; }
    public NpgsqlRange<DateTime> Occupied { get; set; }
    public required string Status { get; set; }
    public DateTime? PendingExpiresAt { get; set; }
}

public sealed class AppointmentAudit
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid? ActorId { get; set; }
    public Guid AppointmentVersion { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Kind { get; set; } = "Created";
}

public sealed class AppointmentStatusCommand
{
    public Guid ActorId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ExpectedVersion { get; set; }
    public required string Status { get; set; }
    public Guid AuditId { get; set; }
}

public sealed class BookingPolicy
{
    public int Id { get; set; }
    public int LeadMinutes { get; set; }
    public int HorizonDays { get; set; }
    public int SlotMinutes { get; set; }
    public int CustomerCancellationLeadMinutes { get; set; } = 120;
    public bool RequiresBusinessApproval { get; set; } = true;
    public Guid Version { get; set; }
}

public sealed class AppointmentMoveCommand
{
    public Guid ActorId { get; set; }
    public Guid RequestKey { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid ExpectedVersion { get; set; }
    public Guid StaffMemberId { get; set; }
    public DateTime StartsAt { get; set; }
    public Guid AuditId { get; set; }
}

public sealed class BookingPolicyAudit
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid PreviousVersion { get; set; }
    public Guid PolicyVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int LeadMinutes { get; set; }
    public int HorizonDays { get; set; }
    public int SlotMinutes { get; set; }
    // Eski audit kayıtlarında müşteriye gösterilmemiş bir koşul üretilmez.
    public int? CustomerCancellationLeadMinutes { get; set; }
    public bool? RequiresBusinessApproval { get; set; }
}
