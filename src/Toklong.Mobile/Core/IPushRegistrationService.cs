namespace Toklong.Mobile.Core;

public interface IPushRegistrationService
{
    Task InitializeAsync(
        CancellationToken cancellationToken = default);

    Task UploadTokenAsync(
        string pushToken,
        CancellationToken cancellationToken = default);

    Task UnregisterAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DisabledPushRegistrationService
    : IPushRegistrationService
{
    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task UploadTokenAsync(
        string pushToken,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task UnregisterAsync(
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
