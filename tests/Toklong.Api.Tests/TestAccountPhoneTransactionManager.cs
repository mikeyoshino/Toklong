using System.Collections.Concurrent;
using Toklong.Application.Abstractions;

namespace Toklong.Api.Tests;

internal sealed class TestAccountPhoneTransactionManager
    : IAccountPhoneTransactionManager
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.Ordinal);
    private readonly List<Lease> leases = [];
    private string? currentPhone;
    private SemaphoreSlim? currentLock;

    public async Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        if (leases.Count > 0)
        {
            if (!string.Equals(
                    currentPhone,
                    normalizedPhone,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Nested test phone transactions must use the same phone.");
            var nested = new Lease();
            leases.Add(nested);
            return new Handle(this, nested);
        }

        var phoneLock = Locks.GetOrAdd(
            normalizedPhone,
            static _ => new SemaphoreSlim(1, 1));
        await phoneLock.WaitAsync(cancellationToken);
        currentPhone = normalizedPhone;
        currentLock = phoneLock;
        var outer = new Lease();
        leases.Add(outer);
        return new Handle(this, outer);
    }

    private void Commit(Lease lease)
    {
        EnsureTop(lease);
        if (lease.Committed)
            throw new InvalidOperationException(
                "The test phone transaction is already committed.");
        lease.Committed = true;
    }

    private void Release(Lease lease)
    {
        EnsureTop(lease);
        leases.RemoveAt(leases.Count - 1);
        lease.Released = true;
        if (leases.Count != 0)
            return;
        currentPhone = null;
        currentLock!.Release();
        currentLock = null;
    }

    private void EnsureTop(Lease lease)
    {
        if (lease.Released ||
            leases.Count == 0 ||
            !ReferenceEquals(leases[^1], lease))
            throw new InvalidOperationException(
                "Test phone transactions must close in LIFO order.");
    }

    private sealed class Lease
    {
        public bool Committed { get; set; }
        public bool Released { get; set; }
    }

    private sealed class Handle(
        TestAccountPhoneTransactionManager owner,
        Lease lease)
        : IAccountPhoneTransaction
    {
        private bool disposed;

        public Task CommitAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            owner.Commit(lease);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
                return ValueTask.CompletedTask;
            owner.Release(lease);
            disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
