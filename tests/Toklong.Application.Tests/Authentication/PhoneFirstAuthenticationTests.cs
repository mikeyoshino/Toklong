using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Authentication;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Security;

namespace Toklong.Application.Tests.Authentication;

public sealed class PhoneFirstAuthenticationTests
{
    private const string ChallengeId = "challenge-001";
    private static readonly string InstallationId =
        Guid.NewGuid().ToString("N");
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly string IdempotencyKey =
        Guid.NewGuid().ToString("N");

    [Fact]
    public async Task New_signup_verification_returns_ticket_without_account()
    {
        await using var database = CreateDatabase();
        var handler = CreateVerifyHandler(database);

        var result = await handler.Handle(
            new VerifyMobileCodeCommand(
                ChallengeId,
                "123456",
                MobileAuthenticationMode.SignUp,
                InstallationId),
            default);

        Assert.Null(result.Session);
        Assert.NotNull(result.Registration);
        Assert.Equal(
            Now.AddMinutes(15),
            result.Registration.ExpiresAt);
        Assert.Empty(database.Buyers);
        var pending =
            Assert.Single(database.PendingMobileRegistrations);
        Assert.NotEqual(
            result.Registration.RegistrationTicket,
            pending.TicketHash);
        Assert.Equal(InstallationId, pending.InstallationId);
    }

    [Fact]
    public async Task Existing_signup_phone_returns_session_profile_after_proof()
    {
        await using var database = CreateDatabase();
        var existingBuyer = BuyerAccount.Create(
            "+66812345678",
            "สมชาย ใจดี",
            "buyer@example.com",
            Now.AddDays(-1));
        database.Buyers.Add(existingBuyer);
        await database.SaveChangesAsync();
        var handler = CreateVerifyHandler(database);

        var result = await handler.Handle(
            new VerifyMobileCodeCommand(
                ChallengeId,
                "123456",
                MobileAuthenticationMode.SignUp,
                InstallationId),
            default);

        Assert.Equal(existingBuyer.Id, result.Session!.BuyerId);
        Assert.Null(result.Registration);
        Assert.Empty(database.PendingMobileRegistrations);
    }

    [Fact]
    public async Task Sign_in_without_an_account_does_not_issue_registration()
    {
        await using var database = CreateDatabase();
        var handler = CreateVerifyHandler(database);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => handler.Handle(
                new VerifyMobileCodeCommand(
                    ChallengeId,
                    "123456",
                    MobileAuthenticationMode.SignIn,
                    null),
                default));

        Assert.Contains("สมัครสมาชิก", exception.Message);
        Assert.Empty(database.PendingMobileRegistrations);
    }

    [Fact]
    public void Registration_ticket_is_opaque_url_safe_and_stored_as_sha256()
    {
        var service = new RegistrationTicketService();

        var ticket = service.Issue();

        Assert.Equal(43, ticket.RawTicket.Length);
        Assert.DoesNotContain(
            ticket.RawTicket,
            character =>
                character is '+' or '/' or '=');
        Assert.Equal(64, ticket.TicketHash.Length);
        Assert.Equal(
            ticket.TicketHash,
            service.Hash(ticket.RawTicket));
    }

    [Fact]
    public async Task Complete_creates_buyer_acceptance_and_consumes_ticket()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);
        var handler = CreateCompletionHandler(database, Now);

        var profile = await handler.Handle(
            ValidCompletion(rawTicket),
            default);

        Assert.Equal("+66812345678", profile.PhoneNumber);
        Assert.Single(database.Buyers);
        Assert.Single(database.MobileAccountTermsAcceptances);
        Assert.NotNull(
            database.PendingMobileRegistrations.Single().ConsumedAt);
    }

    [Fact]
    public async Task Exact_completion_retry_returns_same_buyer()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);
        var handler = CreateCompletionHandler(database, Now);
        var command = ValidCompletion(rawTicket);

        var first = await handler.Handle(command, default);
        var second = await handler.Handle(command, default);

        Assert.Equal(first.BuyerId, second.BuyerId);
        Assert.Single(database.Buyers);
        Assert.Single(database.MobileAccountTermsAcceptances);
    }

    [Fact]
    public async Task Invalid_profile_does_not_consume_registration_ticket()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);
        var handler = CreateCompletionHandler(database, Now);
        var invalid = ValidCompletion(rawTicket) with
        {
            LastName = ""
        };

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(invalid, default));

        Assert.Null(
            database.PendingMobileRegistrations.Single().ConsumedAt);
        Assert.Empty(database.Buyers);
        Assert.Empty(database.MobileAccountTermsAcceptances);
    }

    [Fact]
    public async Task Outdated_terms_do_not_consume_registration_ticket()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);
        var handler = CreateCompletionHandler(database, Now);

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.Handle(
                ValidCompletion(rawTicket) with
                {
                    TermsVersion = "terms-mvp-v0"
                },
                default));

        Assert.Null(
            database.PendingMobileRegistrations.Single().ConsumedAt);
        Assert.Empty(database.Buyers);
        Assert.Empty(database.MobileAccountTermsAcceptances);
    }

    [Fact]
    public async Task Expired_or_other_installation_cannot_complete_registration()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);

        await Assert.ThrowsAsync<DomainException>(
            () => CreateCompletionHandler(database, Now).Handle(
                ValidCompletion(rawTicket) with
                {
                    InstallationId = Guid.NewGuid().ToString("N")
                },
                default));
        await Assert.ThrowsAsync<DomainException>(
            () => CreateCompletionHandler(
                    database,
                    Now.AddMinutes(16))
                .Handle(
                    ValidCompletion(rawTicket),
                    default));

        Assert.Null(
            database.PendingMobileRegistrations.Single().ConsumedAt);
    }

    [Fact]
    public async Task Consumed_ticket_rejects_a_different_idempotency_key()
    {
        await using var database = CreateDatabase();
        var rawTicket = await AddPendingRegistration(database);
        var handler = CreateCompletionHandler(database, Now);
        var command = ValidCompletion(rawTicket);
        await handler.Handle(command, default);

        await Assert.ThrowsAsync<DomainException>(
            () => handler.Handle(
                command with
                {
                    IdempotencyKey = Guid.NewGuid().ToString("N")
                },
                default));
    }

    private static VerifyMobileCodeHandler CreateVerifyHandler(
        ToklongDbContext database) =>
        new(
            new SuccessfulOtpProvider("+66812345678"),
            new BuyerRepository(database),
            new SellerRepository(database),
            new PendingMobileRegistrationRepository(database),
            new RegistrationTicketService(),
            database,
            new FixedClock(Now));

    private static CompleteMobileRegistrationHandler
        CreateCompletionHandler(
            ToklongDbContext database,
            DateTimeOffset now) =>
        new(
            new RegistrationTicketService(),
            new PendingMobileRegistrationRepository(database),
            new BuyerRepository(database),
            database,
            new FixedClock(now));

    private static CompleteMobileRegistrationCommand ValidCompletion(
        string rawTicket) =>
        new(
            rawTicket,
            "สมชาย",
            "ใจดี",
            "buyer@example.com",
            "terms-mvp-v1",
            InstallationId,
            IdempotencyKey);

    private static async Task<string> AddPendingRegistration(
        ToklongDbContext database)
    {
        var ticket = new RegistrationTicketService().Issue();
        database.PendingMobileRegistrations.Add(
            PendingMobileRegistration.Create(
                ticket.TicketHash,
                "+66812345678",
                InstallationId,
                Now,
                Now.AddMinutes(15)));
        await database.SaveChangesAsync();
        return ticket.RawTicket;
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class SuccessfulOtpProvider(string phone)
        : IOtpVerificationProvider
    {
        public Task<OtpChallenge> RequestAsync(
            string phoneNumber,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> VerifyAsync(
            string challengeId,
            string code,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(phone);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
