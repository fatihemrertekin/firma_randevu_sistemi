namespace Server.Features.Scheduling;

public interface IAppointmentReminderDelivery
{
    bool Available { get; }
    decimal MaximumUnitPriceTry { get; }
    Task<BookingSmsResult> SendAsync(string phone, DateTimeOffset startsAt, Guid deliveryId, CancellationToken token);
}

public sealed class DisabledAppointmentReminderDelivery : IAppointmentReminderDelivery
{
    public bool Available => false;
    public decimal MaximumUnitPriceTry => 0;
    public Task<BookingSmsResult> SendAsync(string phone, DateTimeOffset startsAt, Guid deliveryId, CancellationToken token) =>
        throw new InvalidOperationException("Randevu hatırlatma gönderimi kapalı.");
}
