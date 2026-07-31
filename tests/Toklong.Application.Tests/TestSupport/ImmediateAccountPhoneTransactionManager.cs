using Toklong.Application.Abstractions;

namespace Toklong.Application.Tests.TestSupport;

internal sealed class ImmediateAccountPhoneTransactionManager
    : IAccountPhoneTransactionManager
{
    public Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken) =>
        Task.FromResult<IAccountPhoneTransaction>(new Handle());

    private sealed class Handle : IAccountPhoneTransaction
    {
        public Task CommitAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.CompletedTask;
    }
}
