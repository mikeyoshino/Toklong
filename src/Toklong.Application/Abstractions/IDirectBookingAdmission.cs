namespace Toklong.Application.Abstractions;

public interface IDirectBookingAdmission
{
    TimeSpan ProviderTimeout { get; }
    bool TryEnter(out IDisposable lease);
    void RecordProviderSuccess();
    void RecordProviderFailure(
        DateTimeOffset now);
}
