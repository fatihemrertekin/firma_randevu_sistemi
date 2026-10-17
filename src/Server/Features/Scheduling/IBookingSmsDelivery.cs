namespace Server.Features.Scheduling;

public enum BookingSmsOutcome { Accepted, TemporaryFailure, PermanentFailure, Unknown }

public interface IBookingSmsDelivery
{
    bool Available { get; }
    Task<BookingSmsOutcome> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token);
}

internal sealed class DisabledBookingSmsDelivery : IBookingSmsDelivery
{
    public bool Available => false;
    public Task<BookingSmsOutcome> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token) =>
        throw new InvalidOperationException("SMS gönderimi kapalı.");
}
