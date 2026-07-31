using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Api.Security;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Application.Features.Sellers;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Security;

namespace Toklong.Api.Tests.Security;

public sealed class MobileSessionTokenServiceTests
{
    static MobileSessionTokenServiceTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    [Fact]
    public async Task Refresh_token_is_hashed_rotated_and_replay_is_rejected()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var buyer = BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("ผู้ซื้อ", "ทดสอบ"),
            "buyer@example.com",
            DateTimeOffset.UtcNow.AddYears(-1));
        db.Buyers.Add(buyer);
        await db.SaveChangesAsync();
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            new MobileSessionAccountNameReader(db),
            db,
            new ImmediatePhoneTransactions());

        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var stored = Assert.Single(db.MobileSessions);

        Assert.NotEqual(issued.RefreshToken, stored.RefreshTokenHash);
        Assert.Equal(64, stored.RefreshTokenHash.Length);
        Assert.NotNull(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));

        var rotated = await service.RefreshAsync(
            issued.RefreshToken,
            default);

        Assert.NotNull(rotated);
        Assert.NotEqual(issued.RefreshToken, rotated.RefreshToken);
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
        Assert.NotNull(await service.ValidateAccessAsync(
            rotated.AccessToken,
            default));
    }

    [Fact]
    public async Task Logout_revokes_access_token_immediately()
    {
        await using var db = CreateDatabase();
        var repository = new MobileSessionRepository(db);
        var buyer = BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("ผู้ซื้อ", "ทดสอบ"),
            "buyer@example.com",
            DateTimeOffset.UtcNow.AddYears(-1));
        db.Buyers.Add(buyer);
        await db.SaveChangesAsync();
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            repository,
            new MobileSessionAccountNameReader(db),
            db,
            new ImmediatePhoneTransactions());
        var issued = await service.CreateAsync(
            new MobileSessionProfile(
                buyer.Id,
                null,
                "+66812345678",
                "ผู้ซื้อ ทดสอบ"),
            default);
        var session = Assert.Single(db.MobileSessions);

        await service.RevokeAsync(session.Id, default);

        Assert.Null(await service.ValidateAccessAsync(
            issued.AccessToken,
            default));
        Assert.Null(await service.RefreshAsync(
            issued.RefreshToken,
            default));
    }

    [Fact]
    public async Task Session_creation_waits_and_uses_the_current_account_name()
    {
        var connectionString =
            $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connectionString)
            .Options;
        const string phone = "+66812345678";
        var now =
            new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);
        Guid buyerId;
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var buyer = BuyerAccount.Create(
                phone,
                AccountName.Create("สมชาย", "ใจดี"),
                "buyer@example.com",
                now.AddYears(-1));
            buyerId = buyer.Id;
            setup.Buyers.Add(buyer);
            await setup.SaveChangesAsync();
        }

        var transactions = new BlockingPhoneTransactions();
        await using var completion =
            await transactions.BeginAsync(phone, default);
        await using var sessionDatabase = new ToklongDbContext(options);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            new MobileSessionRepository(sessionDatabase),
            new MobileSessionAccountNameReader(sessionDatabase),
            sessionDatabase,
            transactions);
        var staleProfile = new MobileSessionProfile(
            buyerId,
            null,
            phone,
            "สมชาย ใจดี");

        var issuanceTask = service.CreateAsync(
            staleProfile,
            default);
        await transactions.WaiterReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.False(issuanceTask.IsCompleted);

        await using (var completionDatabase =
                     new ToklongDbContext(options))
        {
            var buyer = await completionDatabase.Buyers.SingleAsync();
            buyer.ApplyAccountName(
                AccountName.Create("สมศักดิ์", "ใจดี"),
                now);
            await completionDatabase.SaveChangesAsync();
        }
        await completion.CommitAsync(default);
        await completion.DisposeAsync();

        var issued = await issuanceTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal("สมศักดิ์ ใจดี", issued.DisplayName);
        await using var assertion = new ToklongDbContext(options);
        Assert.Equal(
            "สมศักดิ์ ใจดี",
            (await assertion.MobileSessions.SingleAsync()).DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Otp_sign_in_uses_the_name_committed_after_the_role_was_tracked(
        bool sellerOnly)
    {
        var connectionString =
            $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connectionString)
            .Options;
        const string phone = "+66812345678";
        var now =
            new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            if (sellerOnly)
            {
                setup.Sellers.Add(SellerAccount.Create(
                    phone,
                    now.AddYears(-1),
                    AccountName.Create("สมชาย", "ใจดี")));
            }
            else
            {
                setup.Buyers.Add(BuyerAccount.Create(
                    phone,
                    AccountName.Create("สมชาย", "ใจดี"),
                    "buyer@example.com",
                    now.AddYears(-1)));
            }
            await setup.SaveChangesAsync();
        }

        var transactions = new BlockingPhoneTransactions();
        await using var nameChange =
            await transactions.BeginAsync(phone, default);
        await using var signInDatabase = new ToklongDbContext(options);
        var verification = await new VerifyMobileCodeHandler(
                new SuccessfulOtpProvider(phone),
                new BuyerRepository(signInDatabase),
                new SellerRepository(signInDatabase),
                new PendingMobileRegistrationRepository(signInDatabase),
                new RegistrationTicketService(),
                signInDatabase,
                new FixedClock(now))
            .Handle(
                new VerifyMobileCodeCommand(
                    "challenge-001",
                    "123456",
                    MobileAuthenticationMode.SignIn,
                    null),
                default);
        var profile = Assert.IsType<MobileSessionProfile>(
            verification.Session);
        Assert.Equal("สมชาย ใจดี", profile.DisplayName);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            new MobileSessionRepository(signInDatabase),
            new MobileSessionAccountNameReader(signInDatabase),
            signInDatabase,
            transactions);

        var issuanceTask = service.CreateAsync(profile, default);
        await transactions.WaiterReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        await using (var nameChangeDatabase =
                     new ToklongDbContext(options))
        {
            var newName = AccountName.Create("สมศักดิ์", "ใจดี");
            if (sellerOnly)
            {
                var seller = await nameChangeDatabase.Sellers.SingleAsync();
                seller.ApplyAccountName(newName, now);
            }
            else
            {
                var buyer = await nameChangeDatabase.Buyers.SingleAsync();
                buyer.ApplyAccountName(newName, now);
            }
            await nameChangeDatabase.SaveChangesAsync();
        }
        await nameChange.CommitAsync(default);
        await nameChange.DisposeAsync();

        var issued = await issuanceTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal("สมศักดิ์ ใจดี", issued.DisplayName);
        Assert.Equal(
            "สมชาย ใจดี",
            sellerOnly
                ? signInDatabase.ChangeTracker
                    .Entries<SellerAccount>()
                    .Single()
                    .Entity.DisplayName
                : signInDatabase.ChangeTracker
                    .Entries<BuyerAccount>()
                    .Single()
                    .Entity.FullName);
        await using var assertion = new ToklongDbContext(options);
        Assert.Equal(
            "สมศักดิ์ ใจดี",
            (await assertion.MobileSessions.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task Dual_role_session_is_not_issued_when_current_names_diverge()
    {
        await using var database = CreateDatabase();
        var now = DateTimeOffset.UtcNow;
        var buyer = BuyerAccount.Create(
            "+66812345678",
            AccountName.Create("ชื่อผู้ซื้อ", "ปัจจุบัน"),
            "buyer@example.com",
            now);
        var seller = SellerAccount.Create(
            "+66812345678",
            now,
            AccountName.Create("ชื่อผู้ขาย", "ไม่ตรงกัน"));
        database.AddRange(buyer, seller);
        await database.SaveChangesAsync();
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System,
            new MobileSessionRepository(database),
            new MobileSessionAccountNameReader(database),
            database,
            new ImmediatePhoneTransactions());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new MobileSessionProfile(
                    buyer.Id,
                    seller.Id,
                    "+66812345678",
                    "ชื่อผู้ซื้อ ปัจจุบัน"),
                default));

        Assert.Empty(database.MobileSessions);
    }

    [Fact]
    public async Task Seller_attachment_uses_names_committed_after_roles_were_tracked()
    {
        var connectionString =
            $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseSqlite(connectionString)
            .Options;
        const string phone = "+66812345678";
        var now =
            new DateTimeOffset(2026, 7, 31, 5, 0, 0, TimeSpan.Zero);
        Guid buyerId;
        Guid sellerId;
        Guid sessionId;
        await using (var setup = new ToklongDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var oldName = AccountName.Create("สมชาย", "ใจดี");
            var buyer = BuyerAccount.Create(
                phone,
                oldName,
                "buyer@example.com",
                now.AddYears(-1));
            var seller = SellerAccount.Create(
                phone,
                now.AddYears(-1),
                oldName);
            var session = MobileSession.Create(
                buyer.Id,
                null,
                oldName.DisplayName,
                phone,
                new string('a', 64),
                now,
                now.AddDays(1));
            buyerId = buyer.Id;
            sellerId = seller.Id;
            sessionId = session.Id;
            setup.AddRange(buyer, seller, session);
            await setup.SaveChangesAsync();
        }

        var transactions = new BlockingPhoneTransactions();
        await using var nameChange =
            await transactions.BeginAsync(phone, default);
        await using var attachmentDatabase = new ToklongDbContext(options);
        var staleBuyer = await attachmentDatabase.Buyers
            .SingleAsync(buyer => buyer.Id == buyerId);
        var staleSeller = await new SellerRepository(attachmentDatabase)
            .GetByIdAsync(sellerId, default);
        Assert.NotNull(staleBuyer);
        Assert.Equal("สมชาย ใจดี", staleSeller!.DisplayName);
        var service = new MobileSessionTokenService(
            new EphemeralDataProtectionProvider(),
            new FixedTimeProvider(now),
            new MobileSessionRepository(attachmentDatabase),
            new MobileSessionAccountNameReader(attachmentDatabase),
            attachmentDatabase,
            transactions);

        var attachmentTask = service.AttachSellerAsync(
            sessionId,
            SellerProfile.From(staleSeller),
            default);
        await transactions.WaiterReached.WaitAsync(
            TimeSpan.FromSeconds(5));
        await using (var nameChangeDatabase =
                     new ToklongDbContext(options))
        {
            var newName = AccountName.Create("สมศักดิ์", "ใจดี");
            var buyer = await nameChangeDatabase.Buyers.SingleAsync();
            var seller = await nameChangeDatabase.Sellers.SingleAsync();
            buyer.ApplyAccountName(newName, now);
            seller.ApplyAccountName(newName, now);
            await nameChangeDatabase.SaveChangesAsync();
        }
        await nameChange.CommitAsync(default);
        await nameChange.DisposeAsync();

        var issued = await attachmentTask.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.NotNull(issued);
        Assert.Equal("สมศักดิ์ ใจดี", issued.DisplayName);
        await using var assertion = new ToklongDbContext(options);
        var stored = await assertion.MobileSessions.SingleAsync();
        Assert.Equal(sellerId, stored.SellerId);
        Assert.Equal("สมศักดิ์ ใจดี", stored.DisplayName);
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ToklongDbContext(options);
    }

    private sealed class BlockingPhoneTransactions
        : IAccountPhoneTransactionManager
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly TaskCompletionSource waiterReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaiterReached => waiterReached.Task;

        public async Task<IAccountPhoneTransaction> BeginAsync(
            string normalizedPhone,
            CancellationToken cancellationToken)
        {
            if (!await gate.WaitAsync(0, cancellationToken))
            {
                waiterReached.TrySetResult();
                await gate.WaitAsync(cancellationToken);
            }
            return new Handle(gate);
        }

        private sealed class Handle(SemaphoreSlim gate)
            : IAccountPhoneTransaction
        {
            private int disposed;

            public Task CommitAsync(
                CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ImmediatePhoneTransactions
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

    private sealed class SuccessfulOtpProvider(string phone)
        : IOtpVerificationProvider
    {
        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            OtpPurpose purpose,
            string providerRequestKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(phone);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
