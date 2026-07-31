using System.Net;
using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public abstract class AccountNameChangeViewModelTestBase
{
    protected static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-31T12:00:00+07:00");

    protected static MobileProfile Profile(
        string firstName = "สมชาย",
        string lastName = "ใจดี") =>
        new(
            $"{firstName} {lastName}",
            "0812345678",
            "buyer@example.com",
            null,
            null,
            null,
            true,
            true,
            FirstName: firstName,
            LastName: lastName);

    protected static PendingAccountNameChange Pending(
        Guid? challengeId = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? resendAvailableAt = null,
        int remainingAttempts = 5) =>
        new(
            challengeId ?? Guid.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "08x-xxx-1234",
            "สมศักดิ์",
            "ใจดี",
            expiresAt ?? Now.AddMinutes(10),
            resendAvailableAt ?? Now.AddSeconds(60),
            remainingAttempts);

    protected static VerifiedAccountNameChange Verified() =>
        new(
            "สมศักดิ์",
            "ใจดี",
            "สมศักดิ์ ใจดี",
            Now);

    protected sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    protected sealed class RecordingAnalytics : IMobileAnalytics
    {
        public List<MobileAnalyticsEvent> Events { get; } = [];

        public void Track(MobileAnalyticsEvent value) =>
            Events.Add(value);
    }

    protected sealed class RecordingAuthentication : IAuthenticationService
    {
        public Func<Task<MobileProfile>> GetProfile { get; set; } =
            () => Task.FromResult(Profile());
        public Func<Task<AccountNameChangeEligibility>> GetEligibility { get; set; } =
            () => Task.FromResult(
                new AccountNameChangeEligibility(true, null));
        public Func<Task<PendingAccountNameChange?>> GetPendingName { get; set; } =
            () => Task.FromResult<PendingAccountNameChange?>(null);
        public Func<string, string, Task<PendingAccountNameChange>> RequestName { get; set; } =
            (_, _) => Task.FromResult(Pending());
        public Func<Guid, Task<PendingAccountNameChange>> ResendName { get; set; } =
            _ => Task.FromResult(Pending());
        public Func<Guid, string, Task<VerifiedAccountNameChange>> VerifyName { get; set; } =
            (_, _) => Task.FromResult(Verified());

        public int ProfileCalls { get; private set; }
        public int EligibilityCalls { get; private set; }
        public int PendingNameCalls { get; private set; }
        public bool SignedOut { get; private set; }
        public List<(string FirstName, string LastName)> RequestNameCalls { get; } = [];
        public List<Guid> ResendNameCalls { get; } = [];
        public List<(Guid ChallengeId, string Code)> VerifyNameCalls { get; } = [];
        public List<CancellationToken> RequestTokens { get; } = [];
        public List<CancellationToken> ResendTokens { get; } = [];
        public List<CancellationToken> VerifyTokens { get; } = [];

        public Task<MobileProfile> GetProfileAsync(
            CancellationToken cancellationToken = default)
        {
            ProfileCalls++;
            return GetProfile();
        }

        public Task<AccountNameChangeEligibility>
            GetAccountNameChangeEligibilityAsync(
                CancellationToken cancellationToken = default)
        {
            EligibilityCalls++;
            return GetEligibility();
        }

        public Task<PendingAccountNameChange?>
            GetPendingAccountNameChangeAsync(
                CancellationToken cancellationToken = default)
        {
            PendingNameCalls++;
            return GetPendingName();
        }

        public Task<PendingAccountNameChange>
            RequestAccountNameChangeAsync(
                string firstName,
                string lastName,
                CancellationToken cancellationToken = default)
        {
            RequestNameCalls.Add((firstName, lastName));
            RequestTokens.Add(cancellationToken);
            return RequestName(firstName, lastName);
        }

        public Task<PendingAccountNameChange>
            ResendAccountNameChangeAsync(
                Guid challengeId,
                CancellationToken cancellationToken = default)
        {
            ResendNameCalls.Add(challengeId);
            ResendTokens.Add(cancellationToken);
            return ResendName(challengeId);
        }

        public Task<VerifiedAccountNameChange>
            VerifyAccountNameChangeAsync(
                Guid challengeId,
                string code,
                CancellationToken cancellationToken = default)
        {
            VerifyNameCalls.Add((challengeId, code));
            VerifyTokens.Add(cancellationToken);
            return VerifyName(challengeId, code);
        }

        public Task SignOutAsync(
            CancellationToken cancellationToken = default)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }

        public Task<bool> HasSessionAsync() => throw new NotSupportedException();
        public Task<OtpChallengeResult> RequestCodeAsync(
            string phoneNumber,
            AuthenticationMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AuthenticationVerificationResult> VerifyCodeAsync(
            string challengeId,
            string code,
            AuthenticationMode mode,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task CompleteRegistrationAsync(
            string firstName,
            string lastName,
            string email,
            string termsVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PendingEmailChange?> GetPendingEmailChangeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PendingEmailChange?>(null);
        public Task<PendingEmailChange> RequestEmailChangeAsync(
            string email,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PendingEmailChange> ResendEmailChangeAsync(
            Guid challengeId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string> VerifyEmailChangeAsync(
            Guid challengeId,
            string code,
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    protected static MobileApiRequestException Problem(
        string code,
        DateTimeOffset? nextAllowedAt = null,
        TimeSpan? retryAfter = null,
        int? remainingAttempts = null) =>
        new(
            HttpStatusCode.UnprocessableEntity,
            "private provider detail",
            retryAfter,
            code,
            remainingAttempts: remainingAttempts,
            nextAllowedAt: nextAllowedAt);
}
