namespace Server.Features.Scheduling;

public enum BookingSmsOutcome { Accepted, TemporaryFailure, PermanentFailure, Unknown }
public sealed record BookingSmsResult(BookingSmsOutcome Outcome, string? ProviderReference = null);

public interface IBookingSmsDelivery
{
    bool Available { get; }
    decimal MaximumUnitPriceTry { get; }
    Task<BookingSmsResult> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token);
}
