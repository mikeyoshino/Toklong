namespace Toklong.Application.Abstractions;

public interface IDeviceNotificationRegistrationProvider
{
    Task RegisterAsync(
        string recipientPhoneNumber,
        string installationId,
        string platform,
        string pushToken,
        CancellationToken cancellationToken);

    Task UnregisterAsync(
        string recipientPhoneNumber,
        string installationId,
        CancellationToken cancellationToken);
}
