namespace Toklong.Application.Abstractions;

public interface IAccountPhoneTransactionManager
{
    Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken);
}

public interface IAccountPhoneTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
