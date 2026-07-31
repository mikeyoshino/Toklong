using Toklong.Domain.Authentication;

namespace Toklong.Application.Abstractions;

public interface IPendingMobileRegistrationRepository
{
    Task<string?> GetPhoneByTicketHashAsync(
        string ticketHash,
        CancellationToken cancellationToken);

    Task<PendingMobileRegistration?> GetByTicketHashAsync(
        string ticketHash,
        CancellationToken cancellationToken);

    Task AddAsync(
        PendingMobileRegistration pending,
        CancellationToken cancellationToken);

    Task AddAcceptanceAsync(
        MobileAccountTermsAcceptance acceptance,
        CancellationToken cancellationToken);

    Task<int> DeleteExpiredBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
