using System.Collections.Concurrent;
using Toklong.Application.Abstractions;

namespace Toklong.Api.Tests;

internal sealed class TestAccountPhoneTransactionManager
    : IAccountPhoneTransactionManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks =
        new(StringComparer.Ordinal);

    public async Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var phoneLock = locks.GetOrAdd(
            normalizedPhone,
            static _ => new SemaphoreSlim(1, 1));
        await phoneLock.WaitAsync(cancellationToken);
        return new Handle(phoneLock);
    }

    private sealed class Handle(SemaphoreSlim phoneLock)
        : IAccountPhoneTransaction
    {
        private bool disposed;

        public Task CommitAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
                return ValueTask.CompletedTask;
            disposed = true;
            phoneLock.Release();
            return ValueTask.CompletedTask;
        }
    }
}
