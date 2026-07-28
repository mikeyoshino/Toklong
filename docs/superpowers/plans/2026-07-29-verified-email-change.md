# Verified Email Change Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the direct profile-email update with an authenticated, resumable two-step flow that activates a new payment-contact email only after a valid six-digit code is verified.

**Architecture:** A buyer-owned `BuyerEmailChangeChallenge` aggregate enforces send, expiry, resend, attempt, replay, and supersession rules without touching transaction state. Application handlers coordinate the aggregate, buyer profile, append-only security audit, keyed code hashing, responsive email rendering, and a provider-neutral sender; the mobile app consumes four focused authenticated endpoints and renders separate request and verification pages.

**Tech Stack:** .NET 10, C# 14, MediatR, EF Core 10 with PostgreSQL, ASP.NET Core minimal APIs, .NET MAUI XAML, xUnit.

## Global Constraints

- Only email editing is included; editing first or last name is deferred.
- The current email remains active until the new email is verified.
- A code is exactly six decimal digits, valid for 10 minutes, resendable after 60 seconds, and locked after five incorrect attempts.
- Requesting another code invalidates the earlier code immediately.
- Raw verification codes are never stored in the database, returned by the API, or written to logs.
- Development and test use deterministic code `123456`; that generator must fail closed outside Development and Testing.
- Email remains non-unique payment-contact data and never becomes a login identifier.
- Existing provider payment records, paid transaction receipt destinations, refund records, agreement snapshots, and transaction audit history are never rewritten.
- No transaction, payment, fulfillment, dispute, refund, or payout transition changes in this plan.
- The email template is Thai, includes the exact TOKLONG Transaction Rail brand reference, provides HTML and plain text, is fluid on mobile, and is capped at 600 CSS pixels on desktop.
- Mobile remains one primary action per state and the code control remains one focusable accessible input.
- Do not log full email addresses, phone numbers, raw codes, provider response bodies, or other personal/security data.

## File Structure

### Domain

- Create `src/Toklong.Domain/Buyers/BuyerEmailChangeChallenge.cs` — challenge state, timestamps, verification attempts, replay, and supersession rules.
- Create `src/Toklong.Domain/Buyers/BuyerEmailChangeAuditEvent.cs` — append-only, privacy-minimized security evidence.
- Modify `src/Toklong.Domain/Buyers/BuyerAccount.cs` — add an explicit verified-email activation method and remove the unrestricted mutator when its old handler is removed.
- Create `tests/Toklong.Domain.Tests/Buyers/BuyerEmailChangeChallengeTests.cs` — complete state-rule coverage.

### Application and infrastructure

- Create `src/Toklong.Application/Abstractions/IBuyerEmailChangeRepository.cs` — persistence boundary for challenges and audit events.
- Create `src/Toklong.Application/Abstractions/IEmailVerificationServices.cs` — code, privacy hash, template, and sender contracts.
- Create `src/Toklong.Application/Features/Buyers/BuyerEmailChange.cs` — pending, request, resend, and verify handlers plus response views.
- Create `src/Toklong.Infrastructure/Persistence/BuyerEmailChangeRepository.cs` — EF implementation.
- Create `src/Toklong.Infrastructure/Email/EmailVerificationOptions.cs` — digest key, provider mode, and brand URL configuration.
- Create `src/Toklong.Infrastructure/Email/HmacEmailVerificationCodeService.cs` — secure production code generation and keyed digests.
- Create `src/Toklong.Infrastructure/Email/DevelopmentEmailVerificationCodeService.cs` — guarded deterministic `123456` generator.
- Create `src/Toklong.Infrastructure/Email/ToklongEmailVerificationTemplate.cs` — escaped Thai HTML/plain-text rendering.
- Create `src/Toklong.Infrastructure/Email/DevelopmentTransactionalEmailSender.cs` — bounded in-memory test inbox with no logging or API exposure.
- Create `src/Toklong.Infrastructure/Email/UnavailableTransactionalEmailSender.cs` — fail-closed non-development adapter until a provider is approved.
- Modify `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs` — reject deterministic email verification outside Development/Testing and require a keyed digest secret in production.
- Modify `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs` — DbSets, mappings, constraints, and append-only audit enforcement.
- Modify `src/Toklong.Infrastructure/DependencyInjection.cs` — environment-safe registrations.
- Create `src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.cs`.
- Create `src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.Designer.cs`.
- Modify `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`.
- Create `tests/Toklong.Application.Tests/Buyers/BuyerEmailChangePersistenceTests.cs`.
- Create `tests/Toklong.Application.Tests/Buyers/BuyerEmailChangeHandlerTests.cs`.
- Create `tests/Toklong.Application.Tests/Email/EmailVerificationDeliveryTests.cs`.
- Modify `tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs`.

### API

- Modify `src/Toklong.Api/Api/MobileApi.cs` — remove direct update and map pending/request/resend/verify endpoints.
- Modify `src/Toklong.Api/Program.cs` — add email-change request and verification rate-limit policies.
- Modify `src/Toklong.Api/appsettings.json` — safe unavailable provider defaults and rate-limit values.
- Modify `src/Toklong.Api/appsettings.Development.json` — Development sender, brand URL, and development-only digest key.
- Modify `tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs` — remove direct-update expectations.
- Create `tests/Toklong.Api.Tests/Api/MobileEmailChangeApiTests.cs` — end-to-end authentication, ownership, state, replay, redaction, and immutable-payment-contact coverage.

### Mobile

- Modify `src/Toklong.Mobile/Core/IAuthenticationService.cs` — email-change DTOs and four service operations.
- Modify `src/Toklong.Mobile/Core/IMobileAnalytics.cs` — privacy-safe account-email analytics constructors.
- Modify `src/Toklong.Mobile/Services/MobileAuthenticationService.cs` — authenticated HTTP client implementation.
- Modify `src/Toklong.Mobile/ViewModels/AccountViewModel.cs` — confirmed email, pending status, and navigation only.
- Create `src/Toklong.Mobile/ViewModels/ChangeEmailViewModel.cs` — Step 1 validation, stable request idempotency, and navigation.
- Create `src/Toklong.Mobile/ViewModels/VerifyEmailChangeViewModel.cs` — Step 2 code, expiry, resend, stable verification idempotency, and completion.
- Modify `src/Toklong.Mobile/Pages/AccountPage.xaml` — replace inline direct edit with the approved contact row.
- Create `src/Toklong.Mobile/Pages/ChangeEmailPage.xaml`.
- Create `src/Toklong.Mobile/Pages/ChangeEmailPage.xaml.cs`.
- Create `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml`.
- Create `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml.cs`.
- Modify `src/Toklong.Mobile/AppShell.xaml.cs` — register both routes.
- Modify `src/Toklong.Mobile/MauiProgram.cs` — register pages, view models, and `TimeProvider.System`.
- Modify `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj` — compile the two new view models.
- Create `tests/Toklong.Mobile.Core.Tests/AccountEmailChangeViewModelTests.cs`.
- Create `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`.
- Modify authentication fakes in `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs` and `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`.

---

### Task 1: Domain challenge and verified activation

**Files:**

- Create: `src/Toklong.Domain/Buyers/BuyerEmailChangeChallenge.cs`
- Create: `src/Toklong.Domain/Buyers/BuyerEmailChangeAuditEvent.cs`
- Modify: `src/Toklong.Domain/Buyers/BuyerAccount.cs`
- Create: `tests/Toklong.Domain.Tests/Buyers/BuyerEmailChangeChallengeTests.cs`

**Interfaces:**

- Produces: `BuyerEmailChangeChallenge.Create`, `MarkSendAccepted`, `MarkSendFailed`, `EnsureCanResend`, `Supersede`, and `Verify`.
- Produces: `BuyerEmailVerificationOutcome` values `Verified`, `ExactReplay`, `Incorrect`, and `Locked`.
- Produces: `BuyerAccount.ActivateVerifiedEmail(string email)`.

- [ ] **Step 1: Write failing domain tests**

```csharp
[Fact]
public void Challenge_uses_approved_expiry_and_resend_windows()
{
    var challenge = NewChallenge();

    Assert.Equal(Now.AddMinutes(10), challenge.ExpiresAt);
    Assert.Equal(Now.AddSeconds(60), challenge.ResendAvailableAt);
    Assert.Equal(BuyerEmailChangeStatus.PendingSend, challenge.Status);
}

[Fact]
public void Fifth_wrong_digest_locks_the_challenge()
{
    var challenge = ActiveChallenge();

    for (var attempt = 1; attempt <= 4; attempt++)
        Assert.Equal(
            BuyerEmailVerificationOutcome.Incorrect,
            challenge.Verify(WrongDigest, Guid.NewGuid().ToString("N"), Now));

    Assert.Equal(
        BuyerEmailVerificationOutcome.Locked,
        challenge.Verify(WrongDigest, Guid.NewGuid().ToString("N"), Now));
    Assert.Equal(BuyerEmailChangeStatus.Locked, challenge.Status);
}

[Fact]
public void Exact_completion_replay_is_idempotent()
{
    var challenge = ActiveChallenge();
    var key = Guid.NewGuid().ToString("N");

    Assert.Equal(
        BuyerEmailVerificationOutcome.Verified,
        challenge.Verify(CorrectDigest, key, Now));
    Assert.Equal(
        BuyerEmailVerificationOutcome.ExactReplay,
        challenge.Verify(CorrectDigest, key, Now.AddSeconds(1)));
}

[Fact]
public void Superseded_expired_and_send_failed_challenges_cannot_verify()
{
    Assert.Throws<DomainException>(() =>
        SupersededChallenge().Verify(CorrectDigest, RequestKey, Now));
    Assert.Throws<DomainException>(() =>
        ActiveChallenge().Verify(
            CorrectDigest,
            RequestKey,
            Now.AddMinutes(10)));
    Assert.Throws<DomainException>(() =>
        SendFailedChallenge().Verify(CorrectDigest, RequestKey, Now));
}
```

Also test invalid buyer IDs, non-64-character hex digests, invalid idempotency
keys, `EnsureCanResend` before/after 60 seconds, send acceptance only from
`PendingSend`, and constant-time digest comparison through equal-length decoded
byte arrays.

- [ ] **Step 2: Run the domain tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~BuyerEmailChangeChallengeTests
```

Expected: FAIL because the challenge, status, outcome, audit-event, and verified
activation types do not exist.

- [ ] **Step 3: Implement the aggregate and verified email mutator**

Use these exact public shapes:

```csharp
public enum BuyerEmailChangeStatus
{
    PendingSend,
    Active,
    Verified,
    Expired,
    Locked,
    Superseded,
    SendFailed
}

public enum BuyerEmailVerificationOutcome
{
    Verified,
    ExactReplay,
    Incorrect,
    Locked
}

public sealed class BuyerEmailChangeChallenge
{
    public static BuyerEmailChangeChallenge Create(
        Guid id,
        Guid buyerId,
        string pendingEmail,
        string maskedPendingEmail,
        string codeDigest,
        string requestIdempotencyKey,
        DateTimeOffset createdAt);

    public void MarkSendAccepted(DateTimeOffset acceptedAt);
    public void MarkSendFailed(DateTimeOffset failedAt);
    public void EnsureCanResend(DateTimeOffset now);
    public void Supersede(DateTimeOffset supersededAt);
    public BuyerEmailVerificationOutcome Verify(
        string submittedDigest,
        string verificationIdempotencyKey,
        DateTimeOffset now);
}
```

Set `ExpiresAt` to `createdAt.AddMinutes(10)` and
`ResendAvailableAt` to `createdAt.AddSeconds(60)`. `Verify` must decode the two
validated 64-character hex digests and call
`CryptographicOperations.FixedTimeEquals`. Increment the attempt count only for
an active, unexpired mismatch; change the fifth mismatch to `Locked`.

Add:

```csharp
public void ActivateVerifiedEmail(string email)
{
    var normalized = NormalizeEmail(email);
    if (string.Equals(Email, normalized, StringComparison.OrdinalIgnoreCase))
        throw new DomainException("อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว");
    Email = normalized;
}
```

Keep the old `UpdateEmail` method during Tasks 1–3 so the existing application
project still compiles. Task 4 removes `UpdateBuyerEmailCommand`,
`UpdateBuyerEmailHandler`, and then removes `UpdateEmail` from `BuyerAccount`
in the same commit.

`BuyerEmailChangeAuditEvent` stores `BuyerId`, `ChallengeId`, `Name`,
`DestinationHash`, `MaskedDestination`, `CreatedAt`, and `Result`; validate the
hash as 64 hex characters and never accept a full destination or code property.

- [ ] **Step 4: Run domain tests**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit the domain slice**

```bash
git add src/Toklong.Domain/Buyers/BuyerAccount.cs \
  src/Toklong.Domain/Buyers/BuyerEmailChangeChallenge.cs \
  src/Toklong.Domain/Buyers/BuyerEmailChangeAuditEvent.cs \
  tests/Toklong.Domain.Tests/Buyers/BuyerEmailChangeChallengeTests.cs
git commit -m "feat: add verified email change domain rules"
```

---

### Task 2: Persistence, indexes, and append-only audit

**Files:**

- Create: `src/Toklong.Application/Abstractions/IBuyerEmailChangeRepository.cs`
- Create: `src/Toklong.Infrastructure/Persistence/BuyerEmailChangeRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Create: `tests/Toklong.Application.Tests/Buyers/BuyerEmailChangePersistenceTests.cs`

**Interfaces:**

- Consumes: `BuyerEmailChangeChallenge` and `BuyerEmailChangeAuditEvent`.
- Produces: `IBuyerEmailChangeRepository.GetByIdAsync`,
  `GetOpenByBuyerIdAsync`, `GetByRequestKeyAsync`, `AddAsync`, and
  `AddAuditAsync`.

- [ ] **Step 1: Write failing model and repository tests**

```csharp
[Fact]
public void Model_has_one_pending_or_active_challenge_per_buyer()
{
    using var db = CreateDatabase();
    var entity = db.Model.FindEntityType(
        typeof(BuyerEmailChangeChallenge))!;
    var index = Assert.Single(
        entity.GetIndexes(),
        value => value.IsUnique &&
                 value.Properties.Single().Name ==
                     nameof(BuyerEmailChangeChallenge.BuyerId));

    Assert.Contains("PendingSend", index.GetFilter());
    Assert.Contains("Active", index.GetFilter());
}

[Fact]
public async Task Repository_returns_open_challenge_for_buyer()
{
    await using var db = CreateDatabase();
    var repository = new BuyerEmailChangeRepository(db);
    var active = ActiveChallenge(Now);
    await repository.AddAsync(active, default);
    await db.SaveChangesAsync();

    var found = await repository.GetOpenByBuyerIdAsync(
        active.BuyerId,
        default);

    Assert.Equal(active.Id, found?.Id);
}

[Theory]
[InlineData(EntityState.Modified)]
[InlineData(EntityState.Deleted)]
public async Task Email_change_audit_is_append_only(EntityState state)
{
    await using var db = CreateDatabase();
    db.BuyerEmailChangeAuditEvents.Add(NewAudit());
    await db.SaveChangesAsync();
    db.Entry(db.BuyerEmailChangeAuditEvents.Single()).State = state;

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => db.SaveChangesAsync());
}
```

- [ ] **Step 2: Run persistence tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~BuyerEmailChangePersistenceTests
```

Expected: FAIL because the repository, DbSets, and mappings do not exist.

- [ ] **Step 3: Add the persistence boundary and EF implementation**

```csharp
public interface IBuyerEmailChangeRepository
{
    Task<BuyerEmailChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
    Task<BuyerEmailChangeChallenge?> GetOpenByBuyerIdAsync(
        Guid buyerId,
        CancellationToken cancellationToken);
    Task<BuyerEmailChangeChallenge?> GetByRequestKeyAsync(
        Guid buyerId,
        string requestIdempotencyKey,
        CancellationToken cancellationToken);
    Task AddAsync(
        BuyerEmailChangeChallenge challenge,
        CancellationToken cancellationToken);
    Task AddAuditAsync(
        BuyerEmailChangeAuditEvent auditEvent,
        CancellationToken cancellationToken);
}
```

Map statuses as strings with maximum length 24, digests and destination hashes
as 64 characters, idempotency keys as 32 characters, pending email as 254
characters, masked email as 254 characters, and `Version` as a concurrency
token. Add:

```csharp
challenge.HasIndex(x => x.BuyerId)
    .IsUnique()
    .HasFilter(
        "\"Status\" IN ('PendingSend', 'Active')");
challenge.HasIndex(x => new
{
    x.BuyerId,
    x.RequestIdempotencyKey
}).IsUnique();
challenge.HasOne<BuyerAccount>()
    .WithMany()
    .HasForeignKey(x => x.BuyerId)
    .OnDelete(DeleteBehavior.Restrict);
```

Extend the existing append-only guard so `BuyerEmailChangeAuditEvent` rejects
`Modified` and `Deleted`.

`GetOpenByBuyerIdAsync` queries only `PendingSend` or `Active`. The application
pending-query handler returns a value only when that result is `Active` and
`ExpiresAt > clock.UtcNow`; it hides `PendingSend`, expired, locked, failed,
superseded, and verified rows.

- [ ] **Step 4: Add the deterministic migration and snapshot**

The migration creates `buyer_email_change_challenges` and
`buyer_email_change_audit_events`, their buyer foreign keys, the filtered
unique active index, the request-idempotency unique index, and lookup indexes
on `ExpiresAt` and `ChallengeId`. `Down` drops the audit table before the
challenge table.

Run:

```bash
dotnet build src/Toklong.Infrastructure/Toklong.Infrastructure.csproj
dotnet ef migrations script 20260728190000_PhoneFirstRegistration \
  20260729090000_VerifiedEmailChange \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj
```

Expected: build and migration script generation succeed with both new tables
and no alteration to `transactions`, payment, refund, or payout columns.

- [ ] **Step 5: Run persistence and application tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit the persistence slice**

```bash
git add src/Toklong.Application/Abstractions/IBuyerEmailChangeRepository.cs \
  src/Toklong.Infrastructure/Persistence/BuyerEmailChangeRepository.cs \
  src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs \
  src/Toklong.Infrastructure/DependencyInjection.cs \
  src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.cs \
  src/Toklong.Infrastructure/Persistence/Migrations/20260729090000_VerifiedEmailChange.Designer.cs \
  src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs \
  tests/Toklong.Application.Tests/Buyers/BuyerEmailChangePersistenceTests.cs
git commit -m "feat: persist email verification challenges"
```

---

### Task 3: Secure code, mock sender, and responsive Thai template

**Files:**

- Create: `src/Toklong.Application/Abstractions/IEmailVerificationServices.cs`
- Create: `src/Toklong.Infrastructure/Email/EmailVerificationOptions.cs`
- Create: `src/Toklong.Infrastructure/Email/HmacEmailVerificationCodeService.cs`
- Create: `src/Toklong.Infrastructure/Email/DevelopmentEmailVerificationCodeService.cs`
- Create: `src/Toklong.Infrastructure/Email/ToklongEmailVerificationTemplate.cs`
- Create: `src/Toklong.Infrastructure/Email/DevelopmentTransactionalEmailSender.cs`
- Create: `src/Toklong.Infrastructure/Email/UnavailableTransactionalEmailSender.cs`
- Modify: `src/Toklong.Infrastructure/ProductionConfigurationValidator.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `src/Toklong.Api/appsettings.Development.json`
- Modify: `tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs`
- Create: `tests/Toklong.Application.Tests/Email/EmailVerificationDeliveryTests.cs`

**Interfaces:**

- Produces: `IEmailVerificationCodeService.Issue`, `Digest`, and
  `HashDestination`.
- Produces: `IEmailVerificationTemplate.Render`.
- Produces: `ITransactionalEmailSender.SendAsync`.
- Produces: `IDevelopmentEmailInbox.Messages` for injected tests only.

- [ ] **Step 1: Write failing security and template tests**

```csharp
[Fact]
public void Development_code_is_fixed_but_only_digest_is_persistable()
{
    var service = DevelopmentCodeService();

    var pair = service.Issue(ChallengeId);

    Assert.Equal("123456", pair.Code);
    Assert.Equal(64, pair.Digest.Length);
    Assert.NotEqual(pair.Code, pair.Digest);
    Assert.Equal(
        pair.Digest,
        service.Digest(ChallengeId, "123456"));
}

[Fact]
public void Development_code_service_rejects_production()
{
    Assert.Throws<InvalidOperationException>(() =>
        CreateDevelopmentCodeService("Production"));
}

[Fact]
public void Thai_template_is_responsive_escaped_and_complete_without_image()
{
    var message = Template().Render("123456");

    Assert.Equal(
        "รหัสยืนยันอีเมลใหม่ของคุณจาก TOKLONG",
        message.Subject);
    Assert.Contains("max-width:600px", message.HtmlBody);
    Assert.Contains("width=\"100%\"", message.HtmlBody);
    Assert.Contains("alt=\"TOKLONG\"", message.HtmlBody);
    Assert.Contains("123 456", message.HtmlBody);
    Assert.Contains("123456", message.TextBody);
    Assert.Contains("10 นาที", message.HtmlBody);
    Assert.DoesNotContain("<script", message.HtmlBody);
}

[Fact]
public async Task Development_sender_captures_without_logging_or_http()
{
    var sender = new DevelopmentTransactionalEmailSender(
        new TestEnvironment("Testing"));

    await sender.SendAsync(Message(), default);

    Assert.Single(sender.Messages);
}
```

- [ ] **Step 2: Run delivery tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~EmailVerificationDeliveryTests
```

Expected: FAIL because the services and template do not exist.

- [ ] **Step 3: Define provider-neutral contracts**

```csharp
public sealed record EmailVerificationCodePair(
    string Code,
    string Digest);

public interface IEmailVerificationCodeService
{
    EmailVerificationCodePair Issue(Guid challengeId);
    string Digest(Guid challengeId, string code);
    string HashDestination(string normalizedEmail);
}

public sealed record RenderedEmail(
    string Subject,
    string TextBody,
    string HtmlBody);

public interface IEmailVerificationTemplate
{
    RenderedEmail Render(string code);
}

public sealed record TransactionalEmailMessage(
    string Recipient,
    string Subject,
    string TextBody,
    string HtmlBody,
    string Purpose,
    string CorrelationId,
    string IdempotencyKey);

public sealed record EmailSendAcceptance(string ProviderReference);

public interface ITransactionalEmailSender
{
    Task<EmailSendAcceptance> SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken);
}
```

Use this configuration shape:

```csharp
public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";
    public string Provider { get; init; } = "Unavailable";
    public string DigestKey { get; init; } = "";
    public string BrandLogoUrl { get; init; } = "";
}
```

Use `HMACSHA256` with a configuration key of at least 32 UTF-8 bytes and bind
digests to `challengeId:N`. The secure implementation generates six digits
with `RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")`. The
Development implementation returns `123456` and refuses any environment other
than Development or Testing.

- [ ] **Step 4: Implement the approved template and mock**

Build the HTML with nested presentation tables, inline critical styles,
`role="presentation"`, a fluid `width="100%"` outer table, and an inner
`style="width:100%;max-width:600px"`. Read the exact Transaction Rail PNG URL
from `EmailVerification:BrandLogoUrl`; render both `img alt="TOKLONG"` and a
visible `TOKLONG` wordmark so image blocking loses no instructions.

The bounded mock inbox keeps the newest 50 messages in memory, exposes them
only through the injected `IDevelopmentEmailInbox`, emits no logs, and returns
`dev-email-{CorrelationId}`. The unavailable adapter always throws the typed
plain-language failure `ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง`.

Configuration:

```json
{
  "EmailVerification": {
    "Provider": "Unavailable",
    "BrandLogoUrl": "https://assets.toklong.co.th/email/transaction-rail.png"
  }
}
```

Development overrides `Provider` with `Development` and supplies a
development-only 32-plus-character digest key. Production configuration
validation rejects `Provider=Development` and requires a 32-plus-character
`EmailVerification:DigestKey` supplied outside committed JSON. Add that safe
test value to `ProductionConfigurationValidatorTests.SafeProductionValues`.
Never place a production key in either JSON file.

- [ ] **Step 5: Run security and delivery tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~EmailVerificationDeliveryTests|FullyQualifiedName~ProductionConfigurationValidatorTests"
```

Expected: PASS, including environment guards and template redaction.

- [ ] **Step 6: Commit the delivery slice**

```bash
git add src/Toklong.Application/Abstractions/IEmailVerificationServices.cs \
  src/Toklong.Infrastructure/Email \
  src/Toklong.Infrastructure/DependencyInjection.cs \
  src/Toklong.Infrastructure/ProductionConfigurationValidator.cs \
  src/Toklong.Api/appsettings.json \
  src/Toklong.Api/appsettings.Development.json \
  tests/Toklong.Application.Tests/Email/EmailVerificationDeliveryTests.cs \
  tests/Toklong.Application.Tests/Security/ProductionConfigurationValidatorTests.cs
git commit -m "feat: add mock email verification delivery"
```

---

### Task 4: Application request, resend, pending, and verify handlers

**Files:**

- Create: `src/Toklong.Application/Features/Buyers/BuyerEmailChange.cs`
- Modify: `src/Toklong.Application/Features/Buyers/BuyerOnboarding.cs`
- Modify: `src/Toklong.Domain/Buyers/BuyerAccount.cs`
- Create: `tests/Toklong.Application.Tests/Buyers/BuyerEmailChangeHandlerTests.cs`

**Interfaces:**

- Consumes: buyer and email-change repositories, code service, template,
  sender, unit of work, and clock.
- Produces: `GetPendingBuyerEmailChangeQuery`,
  `RequestBuyerEmailChangeCommand`, `ResendBuyerEmailChangeCommand`, and
  `VerifyBuyerEmailChangeCommand`.
- Produces: `BuyerEmailChangeView`.

- [ ] **Step 1: Write failing handler tests**

```csharp
[Fact]
public async Task Request_keeps_confirmed_email_and_activates_sent_challenge()
{
    var result = await Handler().Handle(
        new RequestBuyerEmailChangeCommand(
            BuyerId,
            "new@example.com",
            RequestKey),
        default);

    Assert.Equal("old@example.com", StoredBuyer.Email);
    Assert.Equal("ne••@example.com", result.MaskedEmail);
    Assert.Equal(BuyerEmailChangeStatus.Active, StoredChallenge.Status);
    Assert.DoesNotContain("123456", Serialize(StoredChallenge));
}

[Fact]
public async Task Resend_supersedes_old_code_and_returns_new_identifier()
{
    Clock.Advance(TimeSpan.FromSeconds(60));

    var replacement = await ResendHandler().Handle(
        new ResendBuyerEmailChangeCommand(
            BuyerId,
            OriginalChallengeId,
            ResendKey),
        default);

    Assert.Equal(BuyerEmailChangeStatus.Superseded, Original.Status);
    Assert.NotEqual(Original.Id, replacement.ChallengeId);
}

[Fact]
public async Task Verify_updates_buyer_and_challenge_in_one_save()
{
    var result = await VerifyHandler().Handle(
        new VerifyBuyerEmailChangeCommand(
            BuyerId,
            ChallengeId,
            "123456",
            VerifyKey),
        default);

    Assert.Equal("new@example.com", result.Email);
    Assert.Equal(BuyerEmailChangeStatus.Verified, Challenge.Status);
}

[Fact]
public async Task Sender_failure_never_exposes_an_active_challenge()
{
    await Assert.ThrowsAsync<DomainException>(() =>
        FailingHandler().Handle(Command(), default));

    Assert.Equal(BuyerEmailChangeStatus.SendFailed, StoredChallenge.Status);
    Assert.Null(await PendingHandler().Handle(
        new GetPendingBuyerEmailChangeQuery(BuyerId),
        default));
}
```

Also cover same-current-email rejection, exact request replay without another
send, wrong-attempt persistence, fifth-attempt lock audit, expiry, cross-buyer
access rejection, exact verification replay, different-key replay rejection,
masked destination, and audit rows containing only masked/keyed destination
evidence.

- [ ] **Step 2: Run handler tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~BuyerEmailChangeHandlerTests
```

Expected: FAIL because the commands and handlers do not exist.

- [ ] **Step 3: Implement the four use cases**

Use these records:

```csharp
public sealed record BuyerEmailChangeView(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public sealed record VerifiedBuyerEmailChangeView(
    string Email,
    DateTimeOffset CompletedAt);

public sealed record GetPendingBuyerEmailChangeQuery(Guid BuyerId)
    : IRequest<BuyerEmailChangeView?>;

public sealed record RequestBuyerEmailChangeCommand(
    Guid BuyerId,
    string Email,
    string IdempotencyKey)
    : IRequest<BuyerEmailChangeView>;

public sealed record ResendBuyerEmailChangeCommand(
    Guid BuyerId,
    Guid ChallengeId,
    string IdempotencyKey)
    : IRequest<BuyerEmailChangeView>;

public sealed record VerifyBuyerEmailChangeCommand(
    Guid BuyerId,
    Guid ChallengeId,
    string Code,
    string IdempotencyKey)
    : IRequest<VerifiedBuyerEmailChangeView>;
```

Request sequence:

1. normalize with `BuyerAccount.NormalizeEmail`;
2. reject equality with the confirmed email;
3. return an exact request replay if the same buyer/key already succeeded;
4. supersede an existing pending/active challenge;
5. create `PendingSend`, save, render, and send;
6. on acceptance mark `Active`, append `account.email_change_requested`, save,
   and return masked metadata;
7. on sender failure mark `SendFailed`, append
   `account.email_change_send_failed`, save, and throw the Thai sender error.

Resend calls `EnsureCanResend`, supersedes first, and follows the same
fail-closed sequence with a new challenge ID and digest. Verify computes the
submitted digest, calls aggregate `Verify`, persists wrong attempts before
returning an error, appends a locked event on the fifth failure, and calls
`buyer.ActivateVerifiedEmail(challenge.PendingEmail)` only for `Verified`.
Persist buyer activation, challenge completion, and
`account.email_change_verified` with one `SaveChangesAsync`.

Resend appends `account.email_change_code_resent`. After these handlers compile
and pass, delete `UpdateBuyerEmailCommand`, `UpdateBuyerEmailHandler`, and
`BuyerAccount.UpdateEmail` so no application-layer bypass remains.

- [ ] **Step 4: Run all application tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit the application slice**

```bash
git add src/Toklong.Application/Features/Buyers/BuyerEmailChange.cs \
  src/Toklong.Application/Features/Buyers/BuyerOnboarding.cs \
  src/Toklong.Domain/Buyers/BuyerAccount.cs \
  tests/Toklong.Application.Tests/Buyers/BuyerEmailChangeHandlerTests.cs
git commit -m "feat: orchestrate verified email changes"
```

---

### Task 5: Authenticated API and bypass removal

**Files:**

- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Api/Program.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs`
- Create: `tests/Toklong.Api.Tests/Api/MobileEmailChangeApiTests.cs`

**Interfaces:**

- Consumes: the four MediatR application messages from Task 4.
- Produces: `GET/POST /api/mobile/me/email-change`,
  `POST /api/mobile/me/email-change/{challengeId}/resend`, and
  `POST /api/mobile/me/email-change/{challengeId}/verify`.

- [ ] **Step 1: Write failing API tests**

```csharp
[Fact]
public async Task Email_stays_old_until_correct_code_is_verified()
{
    using var client = await AuthenticatedBuyerAsync();

    var requested = await client.PostAsJsonAsync(
        "/api/mobile/me/email-change",
        new
        {
            Email = "new@example.com",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });
    requested.EnsureSuccessStatusCode();
    var challenge = await requested.Content
        .ReadFromJsonAsync<MobileEmailChangeResponse>();

    Assert.Equal("old@example.com", await CurrentEmailAsync(client));

    var verified = await client.PostAsJsonAsync(
        $"/api/mobile/me/email-change/{challenge!.ChallengeId}/verify",
        new
        {
            Code = "123456",
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });
    verified.EnsureSuccessStatusCode();

    Assert.Equal("new@example.com", await CurrentEmailAsync(client));
}

[Fact]
public async Task Direct_update_route_no_longer_bypasses_verification()
{
    using var client = await AuthenticatedBuyerAsync();

    var response = await client.PutAsJsonAsync(
        "/api/mobile/me/email",
        new { Email = "bypass@example.com" });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("old@example.com", await CurrentEmailAsync(client));
}

[Fact]
public async Task Responses_and_logs_never_expose_code_or_full_pending_email()
{
    using var client = await AuthenticatedBuyerAsync();
    var response = await RequestAsync(client, "private@example.com");
    var payload = await response.Content.ReadAsStringAsync();

    Assert.DoesNotContain("123456", payload);
    Assert.DoesNotContain("private@example.com", payload);
    Assert.DoesNotContain(
        factory.LogMessages,
        message => message.Contains("123456") ||
                   message.Contains("private@example.com"));
}
```

Also test unauthenticated access, seller-only account rejection, cross-buyer
read/resend/verify denial, 60-second resend enforcement, old-code invalidation,
five-attempt lock, 10-minute expiry, request and verification replay, pending
resume, and unchanged email on a previously paid transaction/provider record.

- [ ] **Step 2: Run API tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileEmailChangeApiTests
```

Expected: FAIL because the new routes are not mapped and the direct route still
exists.

- [ ] **Step 3: Replace the direct endpoint**

Remove:

```csharp
authenticated.MapPut("/me/email", UpdateProfileEmailAsync);
```

Map:

```csharp
authenticated.MapGet(
    "/me/email-change",
    GetPendingEmailChangeAsync);
authenticated.MapPost(
    "/me/email-change",
    RequestEmailChangeAsync)
    .RequireRateLimiting("email-change-request");
authenticated.MapPost(
    "/me/email-change/{challengeId:guid}/resend",
    ResendEmailChangeAsync)
    .RequireRateLimiting("email-change-request");
authenticated.MapPost(
    "/me/email-change/{challengeId:guid}/verify",
    VerifyEmailChangeAsync)
    .RequireRateLimiting("email-change-verify");
```

Every handler derives `BuyerId` from `PartyIds.From(principal)`. DTOs contain
only the exact application fields; pending/request/resend responses expose
masked email, timestamps, and remaining attempts, never `PendingEmail`,
`CodeDigest`, or the raw code.

Use these API records:

```csharp
public sealed record MobileEmailChangeRequest(
    string Email,
    string IdempotencyKey);

public sealed record MobileEmailChangeResendRequest(
    string IdempotencyKey);

public sealed record MobileEmailChangeVerifyRequest(
    string Code,
    string IdempotencyKey);

public sealed record MobileEmailChangeResponse(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public sealed record MobileEmailChangeVerifiedResponse(
    string Email,
    DateTimeOffset CompletedAt);
```

Configure the request policy with a 60-second server window and the verify
policy with a bounded fixed window partitioned by authenticated buyer ID plus
the existing transient HMAC network partition. Do not store the raw network
address.

In `MobileApiFactory`, override
`EmailVerification:Provider=Development` and a 32-plus-character test digest
key so API tests use the same deterministic adapter without exposing an inbox
endpoint.

- [ ] **Step 4: Update the existing authentication API test**

Delete the expectation that `PUT /api/mobile/me/email` changes the profile.
Replace it with an assertion that the route cannot update email and leave the
new behavior entirely in `MobileEmailChangeApiTests`.

- [ ] **Step 5: Run API and domain/application regression tests**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit the API slice**

```bash
git add src/Toklong.Api/Api/MobileApi.cs \
  src/Toklong.Api/Program.cs \
  src/Toklong.Api/appsettings.json \
  tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs \
  tests/Toklong.Api.Tests/Api/MobileEmailChangeApiTests.cs
git commit -m "feat: expose verified email change API"
```

---

### Task 6: Mobile service contract and HTTP client

**Files:**

- Modify: `src/Toklong.Mobile/Core/IAuthenticationService.cs`
- Modify: `src/Toklong.Mobile/Services/MobileAuthenticationService.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

**Interfaces:**

- Produces: `PendingEmailChange`, `RequestEmailChangeAsync`,
  `GetPendingEmailChangeAsync`, `ResendEmailChangeAsync`, and
  `VerifyEmailChangeAsync`.
- Removes: `UpdateEmailAsync`.

- [ ] **Step 1: Change the interface and let compile errors identify all fakes**

```csharp
public sealed record PendingEmailChange(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public interface IAuthenticationService
{
    Task<PendingEmailChange?> GetPendingEmailChangeAsync(
        CancellationToken cancellationToken = default);
    Task<PendingEmailChange> RequestEmailChangeAsync(
        string email,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<PendingEmailChange> ResendEmailChangeAsync(
        Guid challengeId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
    Task<string> VerifyEmailChangeAsync(
        Guid challengeId,
        string code,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
```

Remove `UpdateEmailAsync` from the interface and every fake.

- [ ] **Step 2: Run Mobile.Core tests and verify compile failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: FAIL until `MobileAuthenticationService` and every test fake
implement the four new operations.

- [ ] **Step 3: Implement the authenticated HTTP methods**

Use `MobileApiClient.SendAuthenticatedAsync` for all four methods. Serialize
idempotency keys in their request JSON exactly as the API DTO requires.
`GetPendingEmailChangeAsync` treats HTTP 204 as `null`; all other non-success
responses pass through `MobileApiClient.EnsureSuccessAsync`.

```csharp
public Task<PendingEmailChange> RequestEmailChangeAsync(
    string email,
    string idempotencyKey,
    CancellationToken cancellationToken = default) =>
    SendEmailChangeAsync(
        HttpMethod.Post,
        "api/mobile/me/email-change",
        new
        {
            Email = email.Trim(),
            IdempotencyKey = idempotencyKey
        },
        cancellationToken);
```

Verification returns the confirmed email from the response and never updates a
local profile optimistically.

- [ ] **Step 4: Update fakes and run Mobile.Core tests**

Add explicit `NotSupportedException` implementations to unrelated fakes and
recording behavior only where the new account tests require it.

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit the mobile service slice**

```bash
git add src/Toklong.Mobile/Core/IAuthenticationService.cs \
  src/Toklong.Mobile/Services/MobileAuthenticationService.cs \
  tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs \
  tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs
git commit -m "feat: add mobile email verification client"
```

---

### Task 7: Account entry, two-step UI, analytics, and accessibility

**Files:**

- Modify: `src/Toklong.Mobile/Core/IMobileAnalytics.cs`
- Modify: `src/Toklong.Mobile/ViewModels/AccountViewModel.cs`
- Create: `src/Toklong.Mobile/ViewModels/ChangeEmailViewModel.cs`
- Create: `src/Toklong.Mobile/ViewModels/VerifyEmailChangeViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml`
- Create: `src/Toklong.Mobile/Pages/ChangeEmailPage.xaml`
- Create: `src/Toklong.Mobile/Pages/ChangeEmailPage.xaml.cs`
- Create: `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml`
- Create: `src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Create: `tests/Toklong.Mobile.Core.Tests/AccountEmailChangeViewModelTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs`

**Interfaces:**

- Consumes: Task 6 mobile authentication operations.
- Produces: routes `ChangeEmailPage` and `VerifyEmailChangePage`.
- Produces: analytics constructors with no email, code, or phone properties.

- [ ] **Step 1: Write failing view-model tests**

```csharp
[Fact]
public async Task Account_shows_confirmed_email_and_pending_resume()
{
    var authentication = Authentication(
        profileEmail: "old@example.com",
        pending: Pending());
    var viewModel = Account(authentication);

    await viewModel.LoadAsync();

    Assert.Equal("old@example.com", viewModel.Email);
    Assert.True(viewModel.HasPendingEmailChange);
    Assert.Equal("รอยืนยัน", viewModel.EmailStatus);
}

[Fact]
public async Task Step_one_reuses_idempotency_key_after_network_failure()
{
    var authentication = RecordingAuthentication.FailOnce();
    var viewModel = Change(authentication);
    viewModel.Email = "new@example.com";

    await viewModel.SubmitAsync();
    await viewModel.SubmitAsync();

    Assert.Equal(
        authentication.RequestKeys[0],
        authentication.RequestKeys[1]);
}

[Fact]
public async Task Successful_verification_refreshes_server_profile()
{
    var authentication = RecordingAuthentication.Success();
    var viewModel = Verify(authentication, Pending());
    viewModel.Code = "123456";

    await viewModel.ConfirmAsync();

    Assert.Equal(1, authentication.VerifyCalls);
    Assert.Contains(
        authentication.Analytics.Events,
        value => value.Name == "account_email_change_verified");
    Assert.DoesNotContain(
        authentication.Analytics.Events.SelectMany(
            value => value.Properties.Values),
        value => value.Contains("@") || value.Contains("123456"));
}
```

Also test syntax feedback, non-six-digit code, 60-second countdown from server
time, resend replacing challenge ID, expired and locked copy, one in-flight
action, navigation back to account, sign-out clearing only local navigation
state, and pending restoration from the server.

- [ ] **Step 2: Write failing layout tests**

```csharp
[Fact]
public void Account_has_read_only_email_row_and_no_direct_save()
{
    var xaml = ReadPage("AccountPage.xaml");

    Assert.Contains("ข้อมูลติดต่อ", xaml);
    Assert.Contains("OpenEmailChangeCommand", xaml);
    Assert.DoesNotContain("SaveEmailCommand", xaml);
    Assert.DoesNotContain("บันทึกอีเมล", xaml);
}

[Fact]
public void Verify_email_uses_one_accessible_code_input()
{
    var xaml = ReadPage("VerifyEmailChangePage.xaml");

    Assert.Equal(1, Count(xaml, "<controls:OtpCodeInput"));
    Assert.Contains("ขั้นที่ 2 จาก 2", xaml);
    Assert.Contains("ยืนยันอีเมลใหม่", xaml);
    Assert.Contains("SemanticProperties.Description", xaml);
}
```

- [ ] **Step 3: Run Mobile.Core tests and verify failure**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AccountEmailChangeViewModelTests|FullyQualifiedName~EmailChangeLayoutTests"
```

Expected: FAIL because the view models, pages, routes, and bindings do not
exist.

- [ ] **Step 4: Implement the account row and Step 1**

`AccountViewModel.LoadAsync` fetches profile and pending state concurrently,
then exposes confirmed `Email`, `HasPendingEmailChange`, `EmailStatus`, and
`OpenEmailChangeCommand`. The command routes to Step 2 when pending exists,
otherwise Step 1.

`ChangeEmailViewModel` validates with `MailAddress.TryCreate`, generates one
`Guid.NewGuid().ToString("N")` request key when the email value first becomes
valid, retains that key across a transient retry, and replaces it only when the
email changes or a request succeeds. On success navigate with:

```csharp
await Shell.Current.GoToAsync(
    nameof(VerifyEmailChangePage),
    new Dictionary<string, object>
    {
        ["Pending"] = pending
    });
```

Track `account_email_change_started` with an empty property dictionary.

- [ ] **Step 5: Implement Step 2 and server-derived countdown**

`VerifyEmailChangeViewModel` accepts `PendingEmailChange`, uses injected
`TimeProvider` for countdown display, filters `Code` to six ASCII digits, and
keeps one verification idempotency key while retrying the same code. Changing
the code creates a new key. Resend uses one stable resend key until success,
then replaces the full pending object, clears code, and restarts timing from
the returned timestamps.

On verification success, call `GetProfileAsync`, track
`account_email_change_verified`, navigate to `//main/account`, and let
`AccountPage.OnAppearing` reload server state. Track failures only with coarse
properties `invalid`, `expired`, `locked`, `network`, or `sender`; never pass
exception text to analytics.

Track a successful resend as `account_email_change_code_resent`. Define
`AccountEmailChangeAnalytics` constructors for started, resent, verified, and
failed events, and unit-test that no constructor accepts or emits email, code,
or phone values.

- [ ] **Step 6: Build the approved accessible XAML**

Use the existing `RefinedScreenContent`, `SurfaceCard`, `RefinedInputBorder`,
`RefinedPrimaryButton`, `RefinedInlineButton`, validation styles, and
`OtpCodeInput`. Each page is scrollable under Dynamic Type and soft input.
Each step has exactly one visually primary button. Give the masked destination
a single semantic description and give the resend action a countdown-aware
accessible label.

Register both transient pages/view models, both routes, and:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

Add the two new view-model source links to the Mobile.Core test project.

- [ ] **Step 7: Run mobile tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit the mobile UI slice**

```bash
git add src/Toklong.Mobile/Core/IMobileAnalytics.cs \
  src/Toklong.Mobile/ViewModels/AccountViewModel.cs \
  src/Toklong.Mobile/ViewModels/ChangeEmailViewModel.cs \
  src/Toklong.Mobile/ViewModels/VerifyEmailChangeViewModel.cs \
  src/Toklong.Mobile/Pages/AccountPage.xaml \
  src/Toklong.Mobile/Pages/ChangeEmailPage.xaml \
  src/Toklong.Mobile/Pages/ChangeEmailPage.xaml.cs \
  src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml \
  src/Toklong.Mobile/Pages/VerifyEmailChangePage.xaml.cs \
  src/Toklong.Mobile/AppShell.xaml.cs \
  src/Toklong.Mobile/MauiProgram.cs \
  tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  tests/Toklong.Mobile.Core.Tests/AccountEmailChangeViewModelTests.cs \
  tests/Toklong.Mobile.Core.Tests/EmailChangeLayoutTests.cs
git commit -m "feat: add two-step email change UI"
```

---

### Task 8: Full regression, migration review, and device-ready handoff

**Files:**

- Modify only files required to correct failures found by the commands below.

**Interfaces:**

- Consumes: all prior tasks.
- Produces: one verified end-to-end Development slice ready for API and iPhone
  testing with code `123456`.

- [ ] **Step 1: Run formatting and secret/PII scans**

Run:

```bash
git diff --check
rg -n "123456|new@example\\.com|private@example\\.com" src \
  --glob '!**/DevelopmentEmailVerificationCodeService.cs' \
  --glob '!**/appsettings.Development.json'
rg -n "CodeDigest|PendingEmail" src/Toklong.Api/Api/MobileApi.cs
```

Expected: `git diff --check` succeeds; fixed codes and example addresses do not
appear in production source; API response DTOs contain neither `CodeDigest`
nor `PendingEmail`.

- [ ] **Step 2: Run all non-MAUI test projects**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
```

Expected: PASS with no failed tests.

- [ ] **Step 3: Run mobile core and iOS build checks**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj \
  -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64
```

Expected: Mobile.Core tests pass and the existing configured physical-device
iOS build completes without XAML compilation errors.

- [ ] **Step 4: Review the migration SQL**

Run:

```bash
dotnet ef migrations script 20260728190000_PhoneFirstRegistration \
  20260729090000_VerifiedEmailChange \
  --project src/Toklong.Infrastructure/Toklong.Infrastructure.csproj \
  --startup-project src/Toklong.Api/Toklong.Api.csproj
```

Expected: only email-change challenge/audit tables and indexes are added. No
`UPDATE`, `DELETE`, or `ALTER` statement targets transactions, payment,
refund, payout, agreement snapshot, or existing buyer email values.

- [ ] **Step 5: Run the Development ceremony**

Start the existing PostgreSQL/API development stack, authenticate a buyer,
open Account, request a new email, close and reopen the app, resume the pending
flow, enter `123456`, and verify:

1. the account page shows the old email before confirmation;
2. API/log output contains neither code nor full pending email;
3. resend is unavailable until the server timestamp reaches 60 seconds;
4. requesting a replacement makes the old challenge unusable;
5. successful verification refreshes the account email; and
6. an existing paid transaction retains its original payment/receipt email.

- [ ] **Step 6: Commit verification-only corrections**

If Steps 1–5 required corrections:

```bash
git add -p
git commit -m "fix: complete verified email change checks"
```

If no corrections were required, do not create an empty commit.

## Completion Report Requirements

The implementation handoff must state:

1. what changed;
2. that no transaction or payment state transition changed;
3. tests added or updated and exact commands/results;
4. assumptions, including buyer profile ownership and non-unique email;
5. open provider decisions: production sender, domain authentication,
   bounce/suppression behavior, logo hosting, and production rolling limits;
6. the next smallest vertical slice: implement the approved production email
   adapter without changing domain or mobile contracts.
