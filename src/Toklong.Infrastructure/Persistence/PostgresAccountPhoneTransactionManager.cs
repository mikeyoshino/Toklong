using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Infrastructure.Persistence;

public sealed class PostgresAccountPhoneTransactionManager(
    ToklongDbContext database)
    : IAccountPhoneTransactionManager
{
    private const string NpgsqlProvider =
        "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqliteProvider =
        "Microsoft.EntityFrameworkCore.Sqlite";
    private readonly object sync = new();
    private ScopeState? current;
    private bool starting;

    public Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var phone = ThaiMobilePhone.Normalize(normalizedPhone);
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (current is not null)
            {
                if (!string.Equals(
                        current.Phone,
                        phone,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "A nested account-phone transaction must use the same normalized phone.");
                if (current.Closing ||
                    current.CommitInProgress ||
                    current.PhysicallyCommitted)
                    throw new InvalidOperationException(
                        "The account-phone transaction is no longer available for nesting.");
                var nested = new LeaseState(isOuter: false);
                current.Leases.Add(nested);
                return Task.FromResult<IAccountPhoneTransaction>(
                    new Handle(this, nested));
            }
            if (starting)
                throw new InvalidOperationException(
                    "An account-phone transaction is already starting on this scope.");
            starting = true;
        }

        return BeginOuterAsync(
            phone,
            cancellationToken);
    }

    private async Task<IAccountPhoneTransaction> BeginOuterAsync(
        string phone,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (database.Database.CurrentTransaction is not null)
                throw new InvalidOperationException(
                    "Account-phone transactions must own the outer database transaction.");
            var provider = database.Database.ProviderName;
            if (!string.Equals(
                    provider,
                    NpgsqlProvider,
                    StringComparison.Ordinal) &&
                !string.Equals(
                    provider,
                    SqliteProvider,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Account-phone serialization requires a supported relational provider.");

            transaction =
                await database.Database.BeginTransactionAsync(
                    cancellationToken);
            if (string.Equals(
                    provider,
                    NpgsqlProvider,
                    StringComparison.Ordinal))
            {
                _ = await database.Database
                    .SqlQuery<int>(
                        $"""
                         SELECT 1 AS "Value"
                         FROM pg_advisory_xact_lock(
                             hashtextextended({phone}, 0))
                         """)
                    .SingleAsync(cancellationToken);
            }

            LeaseState outer;
            lock (sync)
            {
                outer = new LeaseState(isOuter: true);
                current = new ScopeState(
                    phone,
                    transaction,
                    outer);
                starting = false;
            }
            return new Handle(this, outer);
        }
        catch
        {
            lock (sync)
                starting = false;
            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                finally
                {
                    await transaction.DisposeAsync();
                }
            }
            throw;
        }
    }

    private async Task CommitAsync(
        LeaseState lease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScopeState scope;
        lock (sync)
        {
            scope = CurrentScopeFor(lease);
            EnsureTopLease(scope, lease);
            if (lease.Committed)
                throw new InvalidOperationException(
                    "This account-phone transaction lease is already committed.");
            if (!lease.IsOuter)
            {
                lease.Committed = true;
                return;
            }
            if (scope.Leases.Count != 1)
                throw new InvalidOperationException(
                    "The outer account-phone transaction cannot commit while nested leases are active.");
            if (scope.Poisoned)
                throw new InvalidOperationException(
                    "The account-phone transaction cannot commit because a participant did not commit.");
            if (scope.CommitInProgress ||
                scope.PhysicallyCommitted ||
                scope.Closing)
                throw new InvalidOperationException(
                    "The outer account-phone transaction cannot commit in its current state.");
            scope.CommitInProgress = true;
        }

        try
        {
            await scope.Transaction.CommitAsync(
                cancellationToken);
        }
        catch
        {
            lock (sync)
            {
                scope.CommitInProgress = false;
                scope.Poisoned = true;
            }
            throw;
        }

        lock (sync)
        {
            scope.CommitInProgress = false;
            scope.PhysicallyCommitted = true;
            lease.Committed = true;
        }
    }

    private async ValueTask ReleaseAsync(LeaseState lease)
    {
        ScopeState scope;
        IDbContextTransaction? transaction = null;
        bool rollback = false;
        lock (sync)
        {
            scope = CurrentScopeFor(lease);
            EnsureTopLease(scope, lease);
            if (scope.CommitInProgress)
                throw new InvalidOperationException(
                    "The account-phone transaction cannot be disposed during commit.");
            if (!lease.Committed)
                scope.Poisoned = true;
            scope.Leases.RemoveAt(scope.Leases.Count - 1);
            lease.Released = true;
            if (lease.IsOuter)
            {
                if (scope.Leases.Count != 0)
                    throw new InvalidOperationException(
                        "The outer account-phone transaction must be disposed last.");
                scope.Closing = true;
                transaction = scope.Transaction;
                rollback = !scope.PhysicallyCommitted;
            }
        }

        if (transaction is null)
            return;

        try
        {
            if (rollback)
                await transaction.RollbackAsync();
        }
        finally
        {
            try
            {
                await transaction.DisposeAsync();
            }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(current, scope))
                        current = null;
                }
            }
        }
    }

    private ScopeState CurrentScopeFor(LeaseState lease)
    {
        if (lease.Released ||
            current is null ||
            !current.Leases.Contains(lease))
            throw new ObjectDisposedException(
                nameof(IAccountPhoneTransaction));
        return current;
    }

    private static void EnsureTopLease(
        ScopeState scope,
        LeaseState lease)
    {
        if (!ReferenceEquals(scope.Leases[^1], lease))
            throw new InvalidOperationException(
                "Account-phone transaction leases must commit and dispose in LIFO order.");
    }

    private sealed class ScopeState(
        string phone,
        IDbContextTransaction transaction,
        LeaseState outer)
    {
        public string Phone { get; } = phone;
        public IDbContextTransaction Transaction { get; } = transaction;
        public List<LeaseState> Leases { get; } = [outer];
        public bool Poisoned { get; set; }
        public bool CommitInProgress { get; set; }
        public bool PhysicallyCommitted { get; set; }
        public bool Closing { get; set; }
    }

    private sealed class LeaseState(bool isOuter)
    {
        public bool IsOuter { get; } = isOuter;
        public bool Committed { get; set; }
        public bool Released { get; set; }
    }

    private sealed class Handle(
        PostgresAccountPhoneTransactionManager owner,
        LeaseState lease) : IAccountPhoneTransaction
    {
        private bool disposed;

        public Task CommitAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return owner.CommitAsync(
                lease,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            try
            {
                await owner.ReleaseAsync(lease);
            }
            finally
            {
                if (lease.Released)
                    disposed = true;
            }
        }
    }
}
