# Verified Account Name Change Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an authenticated mobile user update separate first and last names after confirming a purpose-bound six-digit code sent to the current verified phone, without changing existing transaction snapshots.

**Architecture:** Add a shared structured-name value object to buyer and seller accounts, then implement a dedicated account-name challenge aggregate modeled on the existing verified email-change lifecycle. Server-owned eligibility, durable send limits, provider-managed OTP verification, idempotent commands, and one atomic completion keep the account, roles, sessions, audit history, and immutable transaction evidence consistent.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core minimal APIs, MediatR, EF Core with PostgreSQL migrations and SQLite concurrency tests, .NET MAUI XAML/MVVM, xUnit.

## Global Constraints

- The account page always shows the profile-card `แก้ไข` action and never proactively shows cooldown timing.
- A blocked edit tap shows `ยังเปลี่ยนชื่อไม่ได้` and the exact server-provided next date and time.
- Registration and name changes collect separate `ชื่อ` and `นามสกุล` values.
- The first successful user-initiated change is available immediately; later changes are available at the prior completion time plus two calendar months in `Asia/Bangkok`.
- OTP sends require 60 seconds between accepted sends and allow at most five accepted sends per account/current phone in any 24-hour period.
- Each OTP expires after 10 minutes and locks after five incorrect submissions.
- The existing phone OTP provider owns the issued code; TOKLONG persists its provider challenge reference and only a purpose-bound HMAC digest of submitted codes for replay comparison.
- Never persist or log a raw OTP.
- Reuse `OtpVerificationFormView` and `OtpCodeInput`; do not create another OTP input.
- A successful change updates existing buyer and seller roles and active mobile sessions atomically.
- Never update `SaleTransaction`, party-name snapshots, agreement acceptances, labels, evidence, or hashes from the name-change feature.
- Consumer copy must not claim that the changed name is legal-identity-verified or KYC-verified.
- Analytics must not contain names, phone numbers, OTPs, or free-form exception text.
- Preserve all unrelated working-tree changes and stage only files owned by the current task.

---

## File map

### New focused files

- `src/Toklong.Domain/Accounts/AccountName.cs` — normalization and canonical display name.
- `src/Toklong.Domain/Accounts/AccountNameChangeChallenge.cs` — pending/send/active/verified/expired/locked/superseded lifecycle.
- `src/Toklong.Domain/Accounts/AccountNameChangeAuditEvent.cs` — immutable account audit evidence.
- `src/Toklong.Domain/Accounts/AccountNameVerificationAttempt.cs` — replay-safe submitted-code digest and outcome.
- `src/Toklong.Application/Abstractions/IAccountNameChangeRepository.cs` — challenge, send-count, attempt, audit, and lookup boundary.
- `src/Toklong.Application/Abstractions/IAccountNameVerificationSecurity.cs` — purpose-bound HMAC digest boundary.
- `src/Toklong.Application/Features/Accounts/NameChanges/AccountNameChangeModels.cs` — subject, eligibility, pending, and verified views.
- `src/Toklong.Application/Features/Accounts/NameChanges/GetAccountNameChangeEligibility.cs` — server-owned eligibility.
- `src/Toklong.Application/Features/Accounts/NameChanges/RequestAccountNameChange.cs` — validation, durable quota, provider request, and accepted challenge.
- `src/Toklong.Application/Features/Accounts/NameChanges/ResendAccountNameChangeCode.cs` — replacement challenge and resend rules.
- `src/Toklong.Application/Features/Accounts/NameChanges/GetPendingAccountNameChange.cs` — resumable pending challenge.
- `src/Toklong.Application/Features/Accounts/NameChanges/VerifyAccountNameChange.cs` — provider verification and atomic account completion.
- `src/Toklong.Infrastructure/Persistence/AccountNameChangeRepository.cs` — EF Core implementation.
- `src/Toklong.Infrastructure/Security/AccountNameVerificationSecurity.cs` — HMAC submitted-code digest.
- `src/Toklong.Mobile/Core/AccountNameChange.cs` — mobile records, copy/error mapping, completion state, and analytics factories.
- `src/Toklong.Mobile/Pages/ChangeNamePage.xaml(.cs)` — step 1 form.
- `src/Toklong.Mobile/Pages/VerifyNameChangePage.xaml(.cs)` — step 2 shared OTP form.
- `src/Toklong.Mobile/ViewModels/ChangeNameViewModel.cs` — normalized request and safe navigation.
- `src/Toklong.Mobile/ViewModels/VerifyNameChangeViewModel.cs` — resend/expiry/verification lifecycle.

### Existing files with bounded changes

- `src/Toklong.Domain/Buyers/BuyerAccount.cs` and `src/Toklong.Domain/Sellers/SellerAccount.cs` — structured names and cooldown timestamp.
- `src/Toklong.Domain/Authentication/MobileSession.cs` — safe display-name refresh.
- `src/Toklong.Application/Features/Authentication/CompleteMobileRegistration.cs` — separate registration names.
- Buyer, seller, and session repository interfaces/implementations — party and active-session lookups.
- `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs` plus one generated migration — schema, indexes, concurrency, and legacy backfill.
- OTP provider interface/implementations — explicit `OtpPurpose`.
- `src/Toklong.Api/Api/MobileApi.cs` and `src/Toklong.Api/Program.cs` — contracts, endpoints, and defense-in-depth rate limits.
- Mobile authentication service, registration/account view models, pages, routes, and DI — end-to-end UI.
- Existing test projects and required product/acceptance/implementation docs — coverage and contract alignment.

---

### Task 1: Structured account-name domain model

**Files:**
- Create: `src/Toklong.Domain/Accounts/AccountName.cs`
- Modify: `src/Toklong.Domain/Buyers/BuyerAccount.cs`
- Modify: `src/Toklong.Domain/Sellers/SellerAccount.cs`
- Modify: `src/Toklong.Domain/Authentication/MobileSession.cs`
- Create: `tests/Toklong.Domain.Tests/Accounts/AccountNameTests.cs`
- Modify: `tests/Toklong.Domain.Tests/Authentication/MobileSessionTests.cs`

**Interfaces:**
- Produces: `AccountName.Create(string firstName, string lastName)`, `FirstName`, `LastName`, and `DisplayName`.
- Changes: `BuyerAccount.Create(string phoneNumber, AccountName name, string email, DateTimeOffset verifiedAt)`.
- Changes: `SellerAccount.Create(string phoneNumber, DateTimeOffset verifiedAt, AccountName? name = null)` while preserving its synthetic fallback when `name` is null.
- Produces: `BuyerAccount.ApplyAccountName(AccountName name, DateTimeOffset changedAt)`.
- Produces: `SellerAccount.ApplyAccountName(AccountName name, DateTimeOffset changedAt)`.
- Produces: `MobileSession.UpdateDisplayName(string displayName)`.
- Preserves: existing `BuyerAccount.FullName`, `SellerAccount.DisplayName`, and transaction-facing combined-name contracts.

- [ ] **Step 1: Write failing normalization and eligibility tests**

```csharp
[Theory]
[InlineData("  สมชาย ", " ใจดี  ", "สมชาย", "ใจดี", "สมชาย ใจดี")]
[InlineData("Jean  Luc", "O’Neill-Smith", "Jean Luc", "O’Neill-Smith", "Jean Luc O’Neill-Smith")]
public void Normalizes_supported_names(
    string first, string last, string expectedFirst,
    string expectedLast, string expectedDisplay)
{
    var name = AccountName.Create(first, last);
    Assert.Equal(expectedFirst, name.FirstName);
    Assert.Equal(expectedLast, name.LastName);
    Assert.Equal(expectedDisplay, name.DisplayName);
}

[Theory]
[InlineData("", "ใจดี")]
[InlineData("สมชาย1", "ใจดี")]
[InlineData("สมชาย😀", "ใจดี")]
public void Rejects_missing_or_unsupported_characters(
    string first, string last) =>
    Assert.Throws<DomainException>(() => AccountName.Create(first, last));
```

Also assert a 61-character submitted field fails, a combined name over 120
characters fails, and Thai combining marks are accepted.

- [ ] **Step 2: Run the domain tests and confirm the red state**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter "FullyQualifiedName~AccountNameTests|FullyQualifiedName~MobileSessionTests"
```

Expected: FAIL because `AccountName` and the apply/update methods do not exist.

- [ ] **Step 3: Implement `AccountName` with one normalization path**

```csharp
public sealed record AccountName
{
    public string FirstName { get; }
    public string LastName { get; }
    public string DisplayName => $"{FirstName} {LastName}";

    public static AccountName Create(string firstName, string lastName) =>
        new(NormalizePart(firstName, "ชื่อ"), NormalizePart(lastName, "นามสกุล"));
}
```

Use Unicode categories `UppercaseLetter`, `LowercaseLetter`,
`TitlecaseLetter`, `ModifierLetter`, `OtherLetter`, `NonSpacingMark`, and
`SpacingCombiningMark`; allow internal space, `-`, `'`, and `’`; enforce the
approved lengths after normalization.

- [ ] **Step 4: Add structured fields and explicit domain update methods**

Keep `FullName`/`DisplayName` stored for compatibility. `ApplyAccountName`
assigns structured fields, recomputes the combined name, and sets
`NameChangedAt`. Add an internal migration materializer that can preserve a
legacy part longer than 60 characters without opening that path to new input.

`MobileSession.UpdateDisplayName` must validate non-empty/max-120 input and
increment `Version`.

- [ ] **Step 5: Run the focused domain tests**

Run the Task 1 command again.

Expected: PASS.

- [ ] **Step 6: Commit the domain unit**

```bash
git add src/Toklong.Domain/Accounts/AccountName.cs \
  src/Toklong.Domain/Buyers/BuyerAccount.cs \
  src/Toklong.Domain/Sellers/SellerAccount.cs \
  src/Toklong.Domain/Authentication/MobileSession.cs \
  tests/Toklong.Domain.Tests/Accounts/AccountNameTests.cs \
  tests/Toklong.Domain.Tests/Authentication/MobileSessionTests.cs
git commit -m "feat: add structured account names"
```

---

### Task 2: Persist structured names and align registration

**Files:**
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731090000_StructuredAccountNames.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731090000_StructuredAccountNames.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Modify: `src/Toklong.Application/Features/Authentication/CompleteMobileRegistration.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Mobile/Core/IAuthenticationService.cs`
- Modify: `src/Toklong.Mobile/Services/MobileAuthenticationService.cs`
- Modify: `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml`
- Modify: `tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/CompleteRegistrationNameTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Consumes: `AccountName.Create`.
- Changes: `CompleteMobileRegistrationCommand` and
  `IAuthenticationService.CompleteRegistrationAsync` to accept
  `(string firstName, string lastName, string email, string termsVersion, ...)`.
- Changes API body: `MobileRegistrationCompletion(RegistrationTicket, FirstName, LastName, Email, TermsVersion, InstallationId)`.
- Produces profile fields: `FirstName`, `LastName`, and existing `DisplayName`.

- [ ] **Step 1: Update tests first for the separate registration contract**

Change API helpers to post:

```csharp
new
{
    RegistrationTicket = ticket,
    FirstName = "ผู้ซื้อ",
    LastName = "ทดสอบ",
    Email = "buyer@example.com",
    TermsVersion = termsVersion,
    InstallationId = installationId
}
```

Assert `GET /api/mobile/me` returns all three:

```csharp
Assert.Equal("ผู้ซื้อ", profile.FirstName);
Assert.Equal("ทดสอบ", profile.LastName);
Assert.Equal("ผู้ซื้อ ทดสอบ", profile.DisplayName);
```

Update the XAML test to require separate labels/bindings and reject a combined
`FullName` entry.

- [ ] **Step 2: Run the focused API and mobile tests to confirm failure**

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileAuthenticationApiTests
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AuthenticationLayoutTests|FullyQualifiedName~CompleteRegistrationNameTests"
```

Expected: FAIL because the old contract still accepts `FullName`.

- [ ] **Step 3: Change registration application/API/mobile signatures**

Replace the combined field at every boundary. The handler creates one
`AccountName`, then calls:

```csharp
var buyer = BuyerAccount.Create(
    pending.PhoneNumber,
    AccountName.Create(request.FirstName, request.LastName),
    request.Email,
    clock.UtcNow);
```

The view model validates each field independently and passes normalized values
to `CompleteRegistrationAsync`. Preserve phone-first sign-up and terms
idempotency.

- [ ] **Step 4: Add EF mappings and generate the migration**

Map `FirstName`/`LastName` to max length 120 and `NameChangedAt` nullable on
buyers and sellers. Generate:

```bash
dotnet ef migrations add StructuredAccountNames \
  --project src/Toklong.Infrastructure \
  --startup-project src/Toklong.Api
```

Normalize the generated filename and `[Migration]` ID to
`20260731090000_StructuredAccountNames`, then edit it so it:

1. adds nullable structured columns;
2. backfills buyers by first whitespace boundary without truncation;
3. copies buyer names to a same-phone seller;
4. leaves synthetic/un-splittable seller structured names null;
5. makes buyer structured columns required only after backfill succeeds; and
6. leaves `NameChangedAt` null.

- [ ] **Step 5: Add migration/model assertions**

In API or persistence tests, assert:

```csharp
Assert.Equal(120, buyer.FindProperty(nameof(BuyerAccount.FirstName))!.GetMaxLength());
Assert.True(buyer.FindProperty(nameof(BuyerAccount.NameChangedAt))!.IsNullable);
Assert.Equal(120, seller.FindProperty(nameof(SellerAccount.LastName))!.GetMaxLength());
```

Add a migration SQL test for a normal buyer, a same-phone seller, and synthetic
`ผู้ขาย 1234`; assert no historical display name is rewritten.

- [ ] **Step 6: Run the Task 2 tests and migration validation**

Run both Step 2 commands, then:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~StructuredAccountName
```

Expected: PASS.

- [ ] **Step 7: Commit the registration slice**

Stage only the Task 2 files and commit:

```bash
git commit -m "feat: collect structured registration names"
```

---

### Task 3: Purpose-bound OTP and name-change persistence

**Files:**
- Create: `src/Toklong.Domain/Accounts/AccountNameChangeChallenge.cs`
- Create: `src/Toklong.Domain/Accounts/AccountNameChangeAuditEvent.cs`
- Create: `src/Toklong.Domain/Accounts/AccountNameVerificationAttempt.cs`
- Modify: `src/Toklong.Application/Abstractions/IMobileSessionRepository.cs`
- Create: `src/Toklong.Application/Abstractions/IAccountNameChangeRepository.cs`
- Create: `src/Toklong.Application/Abstractions/IAccountNameVerificationSecurity.cs`
- Modify: `src/Toklong.Application/Abstractions/ISellerRepository.cs`
- Modify: `src/Toklong.Application/Abstractions/IBuyerRepository.cs`
- Modify: `src/Toklong.Application/Abstractions/ISellerRepository.cs`
- Modify: `src/Toklong.Application/Features/Authentication/VerifyMobileCode.cs`
- Modify: `src/Toklong.Application/Features/Buyers/BuyerOnboarding.cs`
- Modify: `src/Toklong.Application/Features/Sellers/SellerOnboarding.cs`
- Modify: `src/Toklong.Infrastructure/Services/DevelopmentOtpVerificationProvider.cs`
- Modify: `src/Toklong.Infrastructure/Services/HttpOtpVerificationProvider.cs`
- Modify: `tests/Toklong.Application.Tests/Authentication/PhoneFirstAuthenticationTests.cs`
- Modify: `tests/Toklong.Application.Tests/Authentication/HttpOtpVerificationProviderTests.cs`
- Modify: `tests/Toklong.Application.Tests/Buyers/BuyerOnboardingTests.cs`
- Create: `src/Toklong.Infrastructure/Persistence/AccountNameChangeRepository.cs`
- Create: `src/Toklong.Infrastructure/Security/AccountNameVerificationSecurity.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731100000_VerifiedAccountNameChange.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260731100000_VerifiedAccountNameChange.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Create: `tests/Toklong.Domain.Tests/Accounts/AccountNameChangeChallengeTests.cs`
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameChangePersistenceTests.cs`

**Interfaces:**
- Produces: `OtpPurpose.MobileAuthentication` and `OtpPurpose.AccountNameChange`.
- Changes: OTP request/verify calls carry `OtpPurpose`; provider challenge IDs remain provider-owned.
- Produces repository methods:

```csharp
Task<AccountNameChangeChallenge?> GetByIdAsync(Guid id, CancellationToken ct);
Task<AccountNameChangeChallenge?> GetOpenAsync(string phoneNumber, CancellationToken ct);
Task<AccountNameChangeChallenge?> GetByRequestKeyAsync(string phoneNumber, string key, CancellationToken ct);
Task<int> CountAcceptedSendsAsync(Guid? buyerId, Guid? sellerId, string phone, DateTimeOffset since, CancellationToken ct);
Task AddAsync(AccountNameChangeChallenge value, CancellationToken ct);
Task AddAttemptAsync(AccountNameVerificationAttempt value, CancellationToken ct);
Task AddAuditAsync(AccountNameChangeAuditEvent value, CancellationToken ct);
```

- Produces: `IAccountNameVerificationSecurity.Digest(Guid challengeId, string code)`.

- [ ] **Step 1: Write challenge lifecycle tests**

Cover pending-send creation, 10-minute expiry, 60-second resend, five incorrect
attempts, exact replay, supersede/send-failure states, and rejection of an empty
provider challenge ID.

```csharp
var challenge = AccountNameChangeChallenge.Create(
    Guid.NewGuid(), buyerId, sellerId, sessionId,
    "+66812345678", "081-•••-5678",
    AccountName.Create("สมศักดิ์", "ใจดี"),
    requestKey, now);
challenge.MarkSendAccepted("provider-challenge", now.AddSeconds(1));
Assert.Equal(AccountNameChangeStatus.Active, challenge.Status);
```

- [ ] **Step 2: Run the new domain tests and confirm failure**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~AccountNameChangeChallengeTests
```

Expected: FAIL because the aggregate does not exist.

- [ ] **Step 3: Implement challenge, audit, and attempt entities**

Use explicit terminal states and a concurrency `Version`. The attempt stores a
64-character HMAC digest, idempotency key, outcome, remaining attempts, and
optional completion time. Audit stores old/new names, subject IDs, session ID,
challenge ID, timestamp, event name, and bounded result.

- [ ] **Step 4: Make OTP purpose explicit without duplicating providers**

Update the provider boundary:

```csharp
public enum OtpPurpose
{
    MobileAuthentication,
    AccountNameChange
}

Task<OtpChallenge> RequestAsync(
    string phoneNumber, OtpPurpose purpose, CancellationToken cancellationToken);
Task<string?> VerifyAsync(
    string challengeId, string code, OtpPurpose purpose, CancellationToken cancellationToken);
```

Update authentication callers to use `MobileAuthentication` and new name
callers to use `AccountNameChange`. Extend provider tests to prove the purpose
is cryptographically bound to the protected ThaiBulkSMS/HTTP challenge and to
the development in-memory challenge. Verification with a different purpose
must return null. Use a 10-minute provider lifetime for `AccountNameChange`
while preserving existing authentication behavior, and prove that no provider
logs the submitted code.

- [ ] **Step 5: Write persistence tests before mappings**

Assert one open challenge per logical account, unique request and verification
keys, provider reference max length, challenge concurrency, 24-hour send
counting, restrictive foreign keys, and repository round trips.

- [ ] **Step 6: Implement repository, security service, mappings, and migration**

Use a filtered unique index on normalized `PhoneNumber` for
`PendingSend`/`Active`, lookup indexes on party IDs and accepted-send time, and
unique attempt provenance. This prevents buyer-role and seller-role sessions
for the same verified phone from opening separate challenges. Register both new
services. Extend the DbContext append-only guard so an existing
`AccountNameChangeAuditEvent` cannot be modified or deleted. Generate:

```bash
dotnet ef migrations add VerifiedAccountNameChange \
  --project src/Toklong.Infrastructure \
  --startup-project src/Toklong.Api
```

Normalize the generated filename and `[Migration]` ID to
`20260731100000_VerifiedAccountNameChange`.

`AccountNameVerificationSecurity` consumes the already secret-managed
`EmailVerificationOptions.DigestKey` but uses a distinct HMAC domain:
`account-name:{challengeId:N}:{code}`. This avoids a second production secret
while keeping account-name attempt digests cryptographically separated from
email code and destination digests.

- [ ] **Step 7: Run domain and persistence tests**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~AccountNameChange
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~AccountNameChangePersistenceTests
```

Expected: PASS.

- [ ] **Step 8: Commit the persistence unit**

```bash
git commit -m "feat: persist verified name change challenges"
```

---

### Task 4: Eligibility, request, pending, and resend application flow

**Files:**
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/AccountNameChangeModels.cs`
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/GetAccountNameChangeEligibility.cs`
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/RequestAccountNameChange.cs`
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/GetPendingAccountNameChange.cs`
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/ResendAccountNameChangeCode.cs`
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeRequestTests.cs`
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeSendLimitTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record AccountNameChangeSubject(
    Guid? BuyerId, Guid? SellerId, Guid SessionId, string PhoneNumber);
public sealed record AccountNameChangeEligibility(
    bool CanChange, DateTimeOffset? NextAllowedAt);
public sealed record PendingAccountNameChange(
    Guid ChallengeId, string MaskedPhoneNumber,
    string FirstName, string LastName,
    DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);
```

- Commands use the authenticated subject and 32-character caller-stable keys.
- Calendar calculation is a single helper that converts to `Asia/Bangkok`,
  applies `AddMonths(2)`, and converts back to UTC.

- [ ] **Step 1: Write failing eligibility tests**

Test null timestamps, one-role and two-role subjects, later-of-two timestamps,
exact boundary, January 31, leap day, and preservation of Bangkok wall-clock
time.

```csharp
Assert.True(await Eligibility(subject, now));
buyer.ApplyAccountName(newName, changedAt);
Assert.Equal(nextAllowed, (await Eligibility(subject, before)).NextAllowedAt);
Assert.True((await Eligibility(subject, nextAllowed)).CanChange);
```

- [ ] **Step 2: Write failing request/send-limit tests**

Assert unchanged names do not call the provider, a sixth accepted send in a
rolling 24 hours is rejected, 60-second resend is enforced, exact request
replay sends once, mismatched key reuse fails, provider rejection marks
`SendFailed`, and unknown outcome remains non-active/reconcilable.

- [ ] **Step 3: Run tests to verify failure**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~AccountNameChangeRequestTests|FullyQualifiedName~AccountNameChangeSendLimitTests"
```

Expected: FAIL because handlers and models do not exist.

- [ ] **Step 4: Implement subject resolution and eligibility**

Reject a subject without buyer and seller IDs, require the current verified
phone to match every loaded role, and return `CanChange=false` plus the exact
instant rather than a generic exception.

- [ ] **Step 5: Implement request, pending, and resend handlers**

Follow the existing email-change two-save pattern: persist `PendingSend`, call
the OTP provider with `OtpPurpose.AccountNameChange`, then persist provider
acceptance or bounded failure evidence. Never return an active pending view
until provider acceptance is stored.

- [ ] **Step 6: Run the focused application tests**

Run the Step 3 command.

Expected: PASS with provider call counts asserted.

- [ ] **Step 7: Commit the request/resend unit**

```bash
git commit -m "feat: request account name verification"
```

---

### Task 5: Atomic verification, role/session sync, and immutable snapshots

**Files:**
- Create: `src/Toklong.Application/Features/Accounts/NameChanges/VerifyAccountNameChange.cs`
- Modify: buyer, seller, and mobile-session repository interfaces/implementations
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeVerificationTests.cs`
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameChangeConcurrencyTests.cs`
- Create: `tests/Toklong.Application.Tests/Accounts/AccountNameTransactionSnapshotTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record VerifiedAccountNameChange(
    string FirstName, string LastName,
    string DisplayName, DateTimeOffset CompletedAt);
```

- Adds:

```csharp
Task<IReadOnlyList<MobileSession>> GetActiveByPartyAsync(
    Guid? buyerId, Guid? sellerId, DateTimeOffset now, CancellationToken ct);
```

- Verification command consumes subject, challenge ID, six-digit code, and
  32-character idempotency key.

- [ ] **Step 1: Write failing atomic-completion tests**

Create a buyer and same-phone seller plus two active sessions. Assert success
updates all four records to one display name and timestamp, marks the challenge,
writes one attempt and one audit event, and calls `SaveChangesAsync` once for
the completion transaction.

- [ ] **Step 2: Add failure and replay tests**

Cover wrong/expired/locked codes, provider phone mismatch, a failure before
commit, exact replay, same key with different code digest, and another
challenge attempting completion inside the newly started cooldown.

- [ ] **Step 3: Add relational concurrency tests**

Use the existing SQLite blocking-save pattern. Two exact requests return one
authoritative result; two different challenges produce one winner. Assert only
one verified audit event and one effective name change.

- [ ] **Step 4: Add immutable transaction tests**

Capture hashes and party names before verification:

```csharp
var originalBuyerName = transaction.BuyerDisplayName;
var originalSellerName = transaction.SellerDisplayName;
var originalAgreementHash = transaction.AgreementCoreSnapshotHash;

await handler.Handle(command, default);

Assert.Equal(originalBuyerName, transaction.BuyerDisplayName);
Assert.Equal(originalSellerName, transaction.SellerDisplayName);
Assert.Equal(originalAgreementHash, transaction.AgreementCoreSnapshotHash);
Assert.Empty(database.Entry(transaction).Properties.Where(p => p.IsModified));
```

Also create a later offer and assert it captures the new name.

- [ ] **Step 5: Run tests to confirm the red state**

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter "FullyQualifiedName~AccountNameChangeVerificationTests|FullyQualifiedName~AccountNameChangeConcurrencyTests|FullyQualifiedName~AccountNameTransactionSnapshotTests"
```

- [ ] **Step 6: Implement minimal atomic verification**

Compute the submitted-code HMAC for replay provenance, invoke provider verify
with `OtpPurpose.AccountNameChange`, require the returned normalized phone to
equal the subject phone, recheck eligibility under concurrency, update roles
and active sessions, append attempt/audit, and commit once. Use bounded
concurrency retries only for exact replay/winner reload; never blindly replay a
provider-changing call.

- [ ] **Step 7: Run the Task 5 tests**

Run the Step 5 command.

Expected: PASS.

- [ ] **Step 8: Commit the completion unit**

```bash
git commit -m "feat: verify and apply account name changes"
```

---

### Task 6: Authenticated mobile API and rate limits

**Files:**
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Modify: `src/Toklong.Api/Program.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Create: `tests/Toklong.Api.Tests/Api/MobileNameChangeApiTests.cs`
- Modify: `tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs`
- Modify: API test factory configuration where rate limits are overridden

**Interfaces:**
- Adds the five endpoints from the design spec.
- Eligibility returns HTTP 200 with `{ CanChange, NextAllowedAt }`.
- Expected validation/cooldown/send-limit/provider errors use consumer-safe
  problem responses and retry metadata; unauthorized/cross-account access never
  reveals a challenge.

- [ ] **Step 1: Write end-to-end API tests first**

Test unauthenticated rejection, first eligibility, request, pending resume,
resend timing, verification, updated profile fields, blocked eligibility with
exact `NextAllowedAt`, cross-account challenge rejection, and exact replay.

- [ ] **Step 2: Add security/limit API tests**

Assert configured authenticated request/verify limiters return 429, durable
five-per-24-hour still applies after a new API factory/scope, cache-control and
`nosniff` headers remain present, and JSON/logs contain neither raw OTP nor
provider exception text.

- [ ] **Step 3: Run API tests to confirm failure**

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter "FullyQualifiedName~MobileNameChangeApiTests|FullyQualifiedName~MobileAuthenticationApiTests"
```

- [ ] **Step 4: Map records, endpoints, and subject extraction**

Build `AccountNameChangeSubject` only from authenticated claims/session. Do not
accept party IDs, session ID, or phone from the request body. Add explicit
request/verify rate-limit policies keyed by authenticated account plus the
existing protected network key.

- [ ] **Step 5: Map consumer-safe responses**

Use stable machine codes such as `name_change_cooldown`,
`name_change_send_limit`, `name_change_expired`, and `name_change_locked`.
Include `nextAllowedAt` or retry seconds only where authorized and relevant.

- [ ] **Step 6: Run the focused API tests**

Run the Step 3 command.

Expected: PASS.

- [ ] **Step 7: Commit the API unit**

```bash
git commit -m "feat: expose verified name change API"
```

---

### Task 7: Mobile contracts, error presentation, and analytics

**Files:**
- Create: `src/Toklong.Mobile/Core/AccountNameChange.cs`
- Modify: `src/Toklong.Mobile/Core/IAuthenticationService.cs`
- Modify: `src/Toklong.Mobile/Core/IMobileAnalytics.cs`
- Modify: `src/Toklong.Mobile/Services/MobileAuthenticationService.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/AccountNameChangePresentationTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/AccountNameChangeAnalyticsTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`

**Interfaces:**
- Produces mobile records for eligibility, pending, and verified results.
- Adds service methods matching the five API endpoints.
- Produces a one-shot `AccountNameChangeCompletionState`.
- Produces:

```csharp
public sealed record AccountNameChangeBlockedNotice(
    DateTimeOffset NextAllowedAt);
```

- Produces bounded analytics factories and exception-to-copy/error-target
  mapping.

- [ ] **Step 1: Write failing serialization and presentation tests**

Assert blocked eligibility carries the exact instant, request bodies trim names
and carry stable idempotency keys, verification retry reuses its key after a
network failure, and stable API codes map to approved Thai copy without
including response internals.

- [ ] **Step 2: Write privacy-safe analytics tests**

Reflect public factory parameters and assert only bounded enums are accepted:

```csharp
Assert.Equal(
    "account_name_change_blocked",
    AccountNameChangeAnalytics.Blocked(
        AccountNameChangeBlockReason.Cooldown).Name);
```

Assert event properties contain no name, phone, code, or free-form message.

- [ ] **Step 3: Run the tests to confirm failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AccountNameChangePresentationTests|FullyQualifiedName~AccountNameChangeAnalyticsTests"
```

- [ ] **Step 4: Implement records, service calls, completion state, and copy**

Follow the existing email-change client pattern, but keep the name feature's
types and completion state separate. Generate a 32-character key with
`Guid.NewGuid().ToString("N")`, retain it across network retries of the same
payload, and replace it only after an authoritative non-network outcome or a
field change.

- [ ] **Step 5: Run the Task 7 tests**

Run the Step 3 command.

Expected: PASS.

- [ ] **Step 6: Commit the mobile core unit**

```bash
git commit -m "feat: add mobile name change contracts"
```

---

### Task 8: Account entry, modal, two-step pages, and shared OTP UI

**Files:**
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/AccountPage.xaml.cs`
- Modify: `src/Toklong.Mobile/ViewModels/AccountViewModel.cs`
- Create: `src/Toklong.Mobile/Pages/ChangeNamePage.xaml`
- Create: `src/Toklong.Mobile/Pages/ChangeNamePage.xaml.cs`
- Create: `src/Toklong.Mobile/Pages/VerifyNameChangePage.xaml`
- Create: `src/Toklong.Mobile/Pages/VerifyNameChangePage.xaml.cs`
- Create: `src/Toklong.Mobile/ViewModels/ChangeNameViewModel.cs`
- Create: `src/Toklong.Mobile/ViewModels/VerifyNameChangeViewModel.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Create: `tests/Toklong.Mobile.Core.Tests/AccountNameChangeLayoutTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/AccountNameChangeViewModelTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/VerifyNameChangeViewModelTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/ViewModelSessionBoundaryTests.cs`

**Interfaces:**
- `AccountViewModel.OpenNameChangeCommand` always remains available when the
  profile is loaded.
- `AccountViewModel.NameChangeBlocked` emits an immutable modal notice for the
  page to present with `DisplayAlert`.
- `ChangeNameViewModel` navigates with a `PendingAccountNameChange`.
- `VerifyNameChangePage` composes `OtpVerificationFormView`.

- [ ] **Step 1: Write layout tests for the approved UI**

Assert the account edit button is inside the blue profile card, contains no
proactive cooldown text, both pages expose one primary action, and verification
XAML contains `OtpVerificationFormView` while containing no `OtpCodeInput`
duplicate outside that component.

- [ ] **Step 2: Write account modal/view-model tests**

Test eligible navigation and blocked event:

```csharp
authentication.Eligibility = new(false, nextAllowedAt);
AccountNameChangeBlockedNotice? notice = null;
viewModel.NameChangeBlocked += (_, value) => notice = value;

await viewModel.OpenNameChangeAsync();

Assert.Equal(nextAllowedAt, notice!.NextAllowedAt);
Assert.Empty(navigation.Routes);
```

Also verify a stale eligibility success is rejected safely by the request
handler and mapped back to the same modal copy.

- [ ] **Step 3: Write form/OTP/session-boundary tests**

Cover separate field errors, unchanged-name handling, stable request keys,
pending navigation recovery, resend/expiry/lock state, exact completion,
one-shot success, navigation failure recovery, sign-out/account switch cleanup,
and late async responses never exposing the previous account.

- [ ] **Step 4: Run mobile tests to confirm failure**

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter "FullyQualifiedName~AccountNameChange|FullyQualifiedName~ViewModelSessionBoundaryTests|FullyQualifiedName~UiLayoutConsistencyTests"
```

- [ ] **Step 5: Implement account entry and modal**

Keep the edit action visible. On tap, fetch eligibility. If blocked, raise the
notice and let `AccountPage` show:

```csharp
await DisplayAlert(
    "ยังเปลี่ยนชื่อไม่ได้",
    $"เพื่อความปลอดภัย ชื่อบัญชีเปลี่ยนได้ทุก 2 เดือน\n\n" +
    $"คุณจะเปลี่ยนได้อีกครั้งวันที่ {formatted}",
    "เข้าใจแล้ว");
```

Use the existing Thai local-date formatting convention and the exact
server-provided instant.

- [ ] **Step 6: Implement both pages and view models**

Reuse email-change page lifetime/session-boundary techniques. Compose the shared
OTP form by binding its code, countdown, resend, confirm, error target, and
semantic descriptions. Do not copy its internal digit-input logic.

- [ ] **Step 7: Register routes/DI and update the core test project links**

Register both pages and view models as transient, both routes by page name, and
every new source file explicitly linked by the mobile core test project where
required.

- [ ] **Step 8: Run the mobile suite and accessibility checks**

Run the Step 4 command, then the full mobile core test project:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 9: Commit the UI unit**

```bash
git commit -m "feat: add verified account name flow"
```

---

### Task 9: Cross-layer verification and documentation

**Files:**
- Modify: `docs/00_PRODUCT_BRIEF.md`
- Modify: `docs/01_USER_FLOWS_AND_STATE_MACHINE.md`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/03_BACKEND_TRANSACTION_RECORD.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `docs/08_IMPLEMENTATION.md`
- Test: all projects in `Toklong.slnx`

**Interfaces:**
- Documents the final implemented contracts only.
- Does not change the approved transaction state machine; account-name audit
  lifecycle is separate from `TransactionState`.

- [ ] **Step 1: Add acceptance statements before the final run**

Document:

- separate registration name fields;
- always-visible account edit action and on-demand cooldown modal;
- first-change and two-calendar-month rules;
- OTP limits and provider purpose;
- buyer/seller/session atomic sync;
- transaction snapshot freeze points; and
- no KYC claim.

Merge carefully with existing edits in these documents; never overwrite
unrelated shipping, payment, or parcel-protection changes.

- [ ] **Step 2: Run formatting and secret/privacy scans**

```bash
git diff --check
rg -n -i \
  "otp.{0,20}(log|analytics)|code.{0,20}(log|analytics)|first.?name.*analytics|last.?name.*analytics" \
  src tests
```

Inspect every match; approved test names and defensive assertions are allowed,
but production logging/analytics of personal data or codes is not.

- [ ] **Step 3: Run focused state, auth, replay, and snapshot tests**

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj \
  --filter FullyQualifiedName~AccountName
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj \
  --filter FullyQualifiedName~AccountName
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj \
  --filter FullyQualifiedName~MobileNameChange
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj \
  --filter FullyQualifiedName~AccountName
```

Expected: all focused tests pass with zero failures.

- [ ] **Step 4: Run the required full verification**

```bash
dotnet restore Toklong.slnx
dotnet build Toklong.slnx --no-restore
dotnet test Toklong.slnx --no-build
```

Expected: restore/build/test exit 0. Record the exact passed/failed/skipped
counts in the completion report.

- [ ] **Step 5: Inspect the final diff against the design**

Verify line by line that:

- no transaction update is reachable from name completion;
- no cooldown text is present proactively on AccountPage;
- the OTP XAML uses the shared component;
- every external call and mutation is idempotent/replay-safe;
- audit and analytics events exist with approved data; and
- all unrelated worktree changes remain intact.

- [ ] **Step 6: Commit documentation and final verification adjustments**

```bash
git commit -m "docs: document verified account names"
```

- [ ] **Step 7: Prepare the completion report**

Report:

1. files and behavior changed;
2. account lifecycle rules and unchanged transaction snapshot transitions;
3. exact tests added/updated and final command results;
4. assumptions, especially the provider purpose-bound OTP mapping;
5. provider/operations blockers; and
6. the next smallest vertical slice.
