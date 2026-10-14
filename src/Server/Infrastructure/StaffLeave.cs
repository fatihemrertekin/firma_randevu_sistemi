namespace Server.Infrastructure;

public sealed class StaffLeave
{
    public Guid Id { get; set; }
    public Guid StaffMemberId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public required string RequestHash { get; set; }
    // Silinen kimlik yeniden farklı içerikle oluşturulamaz; tekrar sözleşmesi korunur.
    public bool IsRemoved { get; set; }
}
