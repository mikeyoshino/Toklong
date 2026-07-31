using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Infrastructure.Persistence;

public sealed class PostgresAccountPhoneTransactionManager(
    ToklongDbContext database)
    : IAccountPhoneTransactionManager
{
    public async Task<IAccountPhoneTransaction> BeginAsync(
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var phone = ThaiMobilePhone.Normalize(normalizedPhone);
        if (database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "An account-phone transaction is already active.");
        if (!string.Equals(
                database.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Account-phone serialization requires PostgreSQL.");

        var transaction =
            await database.Database.BeginTransactionAsync(
                cancellationToken);
        try
        {
            _ = await database.Database
                .SqlQuery<int>(
                    $"""
                     SELECT 1 AS "Value"
                     FROM pg_advisory_xact_lock(
                         hashtextextended({phone}, 0))
                     """)
                .SingleAsync(cancellationToken);
            return new Handle(transaction);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class Handle(IDbContextTransaction transaction)
        : IAccountPhoneTransaction
    {
        private bool committed;
        private bool disposed;

        public async Task CommitAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (committed)
                return;
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            disposed = true;
            if (!committed)
                await transaction.RollbackAsync();
            await transaction.DisposeAsync();
        }
    }
}
