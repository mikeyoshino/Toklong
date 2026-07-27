# Welcome and Thai Phone Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved centered Welcome page and secure Thai phone-first sign-in/sign-up flow, including SMS verification, resumable one-time registration tickets, atomic account completion, and mobile accessibility.

**Architecture:** Preserve the existing OTP provider and mobile-session implementation. Split code verification from account creation: a verified new sign-up receives a 15-minute opaque ticket whose hash is persisted, while profile completion consumes that ticket and creates the buyer, immutable account-terms acceptance, and mobile session inside one explicit database transaction. The MAUI client uses shared centered-brand and Thai-phone controls, stores pending registration only in secure storage, and lets startup route a valid pending registration to profile completion.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, MediatR, EF Core/Npgsql, .NET MAUI XAML, xUnit, platform SecureStorage.

## Global Constraints

- Read and preserve the repository domain rules in `AGENTS.md` and `docs/00_PRODUCT_BRIEF.md` through `docs/07_REGULATORY_SOURCE_NOTES.md`.
- Accept only Thai mobile numbers in visible local form `0XX-XXX-XXXX`; do not show a country picker, flag, or `+66`.
- Normalize phone values to E.164 only at the service boundary; do not log raw phone, email, OTP, challenge, or registration ticket.
- Keep sign-in and sign-up as explicit routes; do not add email/password, social login, anonymous sessions, a WebView, or an auth UI dependency.
- Registration tickets use at least 256 random bits, are stored only as SHA-256 hashes, expire after exactly 15 minutes, bind to sign-up purpose and installation ID, and complete registration once.
- Account, terms acceptance, registration-ticket consumption, and mobile-session persistence commit atomically.
- Preserve the approved Transaction Rail asset and 1.2-second startup logo-build animation.
- New account acceptance uses terms version `terms-mvp-v1`; the API rejects any other version.
- Terms and Privacy links use `https://toklong.co.th/terms` and `https://toklong.co.th/privacy`; deployment of legally approved page content remains a release dependency and must be reported.
- No transaction, payment, fulfillment, dispute, refund, or payout state or rule changes.
- Every task follows red-green-refactor and stages only its listed files; preserve unrelated working-tree changes.

---

### Task 0: Checkpoint the approved Transaction Rail startup baseline

**Files:**
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`
- Modify: `landing.html`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `src/Toklong.Mobile/Resources/AppIcon/appiconfg.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/brand_mark.svg`
- Modify: `src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg`
- Modify: `src/Toklong.Mobile/Resources/Splash/splash.svg`
- Create: `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml`
- Create: `src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml.cs`
- Create: `src/Toklong.Mobile/Core/StartupCoordinator.cs`
- Create: `src/Toklong.Mobile/Pages/StartupLogoPage.xaml`
- Create: `src/Toklong.Mobile/Pages/StartupLogoPage.xaml.cs`
- Create: `src/Toklong.Mobile/Resources/Images/brand_confirmation_node.svg`
- Create: `src/Toklong.Mobile/Resources/Images/brand_rail_lower.svg`
- Create: `src/Toklong.Mobile/Resources/Images/brand_rail_upper.svg`
- Create: `src/Toklong.Mobile/Services/StartupMotionPreference.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj`
- Modify: `tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`

**Interfaces:**
- Produces the approved exact Transaction Rail assets, `StartupCoordinator`,
  `StartupLogoPage`, and reduced-motion startup behavior that later tasks
  preserve and extend.

- [ ] **Step 1: Verify the already-implemented startup slice**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj -t:Compile -p:TargetFrameworks=net10.0-ios -p:TargetFramework=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -p:NuGetAudit=false
git diff --check
```

Expected: mobile core tests pass, XAML compile succeeds, and the diff has no
whitespace errors. Do not change behavior in this checkpoint task.

- [ ] **Step 2: Commit only the approved startup files**

```bash
git add docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md landing.html src/Toklong.Mobile/App.xaml.cs src/Toklong.Mobile/MauiProgram.cs src/Toklong.Mobile/Resources/AppIcon/appiconfg.svg src/Toklong.Mobile/Resources/Images/brand_mark.svg src/Toklong.Mobile/Resources/Images/ui_ai_assist.svg src/Toklong.Mobile/Resources/Splash/splash.svg src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml src/Toklong.Mobile/Controls/TransactionRailMarkView.xaml.cs src/Toklong.Mobile/Core/StartupCoordinator.cs src/Toklong.Mobile/Pages/StartupLogoPage.xaml src/Toklong.Mobile/Pages/StartupLogoPage.xaml.cs src/Toklong.Mobile/Resources/Images/brand_confirmation_node.svg src/Toklong.Mobile/Resources/Images/brand_rail_lower.svg src/Toklong.Mobile/Resources/Images/brand_rail_upper.svg src/Toklong.Mobile/Services/StartupMotionPreference.cs tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj tests/Toklong.Mobile.Core.Tests/UiLayoutConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/BrandAssetConsistencyTests.cs tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs
git commit -m "feat: add transaction rail startup motion"
```

### Task 1: Pending registration and immutable account-terms domain records

**Files:**
- Create: `src/Toklong.Domain/Authentication/PendingMobileRegistration.cs`
- Create: `src/Toklong.Domain/Authentication/MobileAccountTermsAcceptance.cs`
- Create: `tests/Toklong.Domain.Tests/Authentication/PendingMobileRegistrationTests.cs`
- Create: `tests/Toklong.Domain.Tests/Authentication/MobileAccountTermsAcceptanceTests.cs`

**Interfaces:**
- Produces:
  `PendingMobileRegistration.Create(string ticketHash, string phoneNumber, string installationId, DateTimeOffset now, DateTimeOffset expiresAt)`.
- Produces:
  `RegistrationCompletionStatus PendingMobileRegistration.ValidateCompletion(string installationId, string idempotencyKey, DateTimeOffset now)`.
- Produces:
  `void PendingMobileRegistration.Complete(Guid buyerId, string idempotencyKey, DateTimeOffset completedAt)`.
- Produces:
  `MobileAccountTermsAcceptance.Create(Guid buyerId, string termsVersion, string installationId, string idempotencyKey, DateTimeOffset acceptedAt)`.

- [ ] **Step 1: Write failing pending-registration tests**

```csharp
[Fact]
public void Create_requires_sha256_hash_and_future_expiry()
{
    Assert.Throws<DomainException>(() =>
        PendingMobileRegistration.Create(
            "raw-ticket",
            "+66812345678",
            Guid.NewGuid().ToString("N"),
            Now,
            Now.AddMinutes(15)));
}

[Fact]
public void Complete_is_one_time_but_exact_retry_is_recognized()
{
    var idempotencyKey = Guid.NewGuid().ToString("N");
    var pending = NewPending();
    pending.Complete(BuyerId, idempotencyKey, Now.AddMinutes(1));

    Assert.Equal(
        RegistrationCompletionStatus.ExactReplay,
        pending.ValidateCompletion(
            pending.InstallationId,
            idempotencyKey,
            Now.AddMinutes(2)));
    Assert.Throws<DomainException>(() =>
        pending.ValidateCompletion(
            pending.InstallationId,
            Guid.NewGuid().ToString("N"),
            Now.AddMinutes(2)));
}

[Fact]
public void ValidateCompletion_rejects_expiry_and_installation_mismatch()
{
    var pending = NewPending();
    Assert.Throws<DomainException>(() =>
        pending.ValidateCompletion(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            Now.AddMinutes(1)));
    Assert.Throws<DomainException>(() =>
        pending.ValidateCompletion(
            pending.InstallationId,
            Guid.NewGuid().ToString("N"),
            Now.AddMinutes(16)));
}
```

- [ ] **Step 2: Run the pending-registration tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --filter FullyQualifiedName~PendingMobileRegistrationTests
```

Expected: FAIL because `PendingMobileRegistration` and
`RegistrationCompletionStatus` do not exist.

- [ ] **Step 3: Implement the pending-registration aggregate**

```csharp
public enum RegistrationCompletionStatus
{
    Ready,
    ExactReplay
}

public sealed class PendingMobileRegistration
{
    private PendingMobileRegistration() { }

    public Guid Id { get; private set; }
    public string TicketHash { get; private set; } = "";
    public string PhoneNumber { get; private set; } = "";
    public string InstallationId { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public string? CompletionIdempotencyKey { get; private set; }
    public Guid? BuyerId { get; private set; }
    public long Version { get; private set; }

    public RegistrationCompletionStatus ValidateCompletion(
        string installationId,
        string idempotencyKey,
        DateTimeOffset now)
    {
        if (ConsumedAt.HasValue)
            return InstallationId == installationId &&
                   CompletionIdempotencyKey == idempotencyKey &&
                   BuyerId.HasValue
                ? RegistrationCompletionStatus.ExactReplay
                : throw new DomainException(
                    "ลิงก์สมัครสมาชิกนี้ถูกใช้แล้ว กรุณาเริ่มใหม่");
        if (ExpiresAt <= now || InstallationId != installationId)
            throw new DomainException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");
        return RegistrationCompletionStatus.Ready;
    }
}
```

Validate lowercase 64-character hex hashes, normalized E.164 Thai mobile
numbers, 32-character normalized GUID installation IDs, and 32-character UUID
idempotency keys. `Complete` sets consumption fields once and increments the
concurrency token through EF.

- [ ] **Step 4: Write failing immutable account-terms tests**

```csharp
[Fact]
public void Create_records_exact_account_terms_evidence()
{
    var acceptance = MobileAccountTermsAcceptance.Create(
        BuyerId,
        "terms-mvp-v1",
        InstallationId,
        IdempotencyKey,
        Now);

    Assert.Equal(BuyerId, acceptance.BuyerId);
    Assert.Equal("terms-mvp-v1", acceptance.TermsVersion);
    Assert.Equal(InstallationId, acceptance.InstallationId);
    Assert.Equal(IdempotencyKey, acceptance.IdempotencyKey);
    Assert.Equal(Now, acceptance.AcceptedAt);
}
```

- [ ] **Step 5: Implement the immutable account-terms record**

Create `MobileAccountTermsAcceptance` with private setters, a private EF
constructor, and the factory shown above. Require non-empty values, cap
`TermsVersion` at 40 characters, and normalize both GUID strings to lowercase
`N` form. Do not add mutation methods.

- [ ] **Step 6: Run focused domain tests**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj --filter "FullyQualifiedName~PendingMobileRegistrationTests|FullyQualifiedName~MobileAccountTermsAcceptanceTests"
```

Expected: PASS.

- [ ] **Step 7: Commit the domain slice**

```bash
git add src/Toklong.Domain/Authentication/PendingMobileRegistration.cs src/Toklong.Domain/Authentication/MobileAccountTermsAcceptance.cs tests/Toklong.Domain.Tests/Authentication/PendingMobileRegistrationTests.cs tests/Toklong.Domain.Tests/Authentication/MobileAccountTermsAcceptanceTests.cs
git commit -m "feat: model pending mobile registration"
```

### Task 2: Registration persistence, append-only protection, and migration

**Files:**
- Create: `src/Toklong.Application/Abstractions/IPendingMobileRegistrationRepository.cs`
- Create: `src/Toklong.Infrastructure/Persistence/PendingMobileRegistrationRepository.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260728190000_PhoneFirstRegistration.cs`
- Create: `src/Toklong.Infrastructure/Persistence/Migrations/20260728190000_PhoneFirstRegistration.Designer.cs`
- Modify: `src/Toklong.Infrastructure/Persistence/Migrations/ToklongDbContextModelSnapshot.cs`
- Create: `tests/Toklong.Application.Tests/Authentication/PendingRegistrationPersistenceTests.cs`

**Interfaces:**
- Consumes: Task 1 domain types.
- Produces:
  `Task<PendingMobileRegistration?> GetByTicketHashAsync(string ticketHash, CancellationToken cancellationToken)`.
- Produces:
  `Task AddAsync(PendingMobileRegistration pending, CancellationToken cancellationToken)`.
- Produces:
  `Task AddAcceptanceAsync(MobileAccountTermsAcceptance acceptance, CancellationToken cancellationToken)`.
- Produces:
  `Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write failing EF model and append-only tests**

```csharp
[Fact]
public void Model_has_unique_ticket_hash_and_account_acceptance_idempotency()
{
    using var database = CreateDatabase();
    var pending = database.Model
        .FindEntityType(typeof(PendingMobileRegistration))!;
    Assert.Contains(
        pending.GetIndexes(),
        index => index.IsUnique &&
                 index.Properties.Single().Name ==
                 nameof(PendingMobileRegistration.TicketHash));

    var acceptance = database.Model
        .FindEntityType(typeof(MobileAccountTermsAcceptance))!;
    Assert.Contains(
        acceptance.GetIndexes(),
        index => index.IsUnique &&
                 index.Properties.Select(x => x.Name)
                     .SequenceEqual([
                         nameof(MobileAccountTermsAcceptance.BuyerId),
                         nameof(MobileAccountTermsAcceptance.TermsVersion)
                     ]));
}

[Fact]
public async Task Account_terms_acceptance_cannot_be_modified_or_deleted()
{
    await using var database = CreateDatabase();
    database.MobileAccountTermsAcceptances.Add(NewAcceptance());
    await database.SaveChangesAsync();
    database.Remove(database.MobileAccountTermsAcceptances.Single());
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => database.SaveChangesAsync());
}
```

- [ ] **Step 2: Run persistence tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~PendingRegistrationPersistenceTests
```

Expected: FAIL because DbSets and repository do not exist.

- [ ] **Step 3: Add repository contract and EF mappings**

Add DbSets named `PendingMobileRegistrations` and
`MobileAccountTermsAcceptances`. Map tables
`pending_mobile_registrations` and `mobile_account_terms_acceptances`.
Configure:

```csharp
pending.HasIndex(x => x.TicketHash).IsUnique();
pending.HasIndex(x => x.ExpiresAt);
pending.Property(x => x.TicketHash).HasMaxLength(64);
pending.Property(x => x.PhoneNumber).HasMaxLength(16);
pending.Property(x => x.InstallationId).HasMaxLength(32);
pending.Property(x => x.CompletionIdempotencyKey).HasMaxLength(32);
pending.Property(x => x.Version).IsConcurrencyToken();

accountAcceptance.HasIndex(x => new
{
    x.BuyerId,
    x.TermsVersion
}).IsUnique();
accountAcceptance.HasIndex(x => x.IdempotencyKey).IsUnique();
accountAcceptance.Property(x => x.TermsVersion).HasMaxLength(40);
accountAcceptance.Property(x => x.InstallationId).HasMaxLength(32);
accountAcceptance.Property(x => x.IdempotencyKey).HasMaxLength(32);
```

Extend the existing SaveChanges guards so modified or deleted
`MobileAccountTermsAcceptance` rows throw before persistence.

- [ ] **Step 4: Implement repository and cleanup query**

```csharp
public Task<int> DeleteExpiredBeforeAsync(
    DateTimeOffset cutoff,
    CancellationToken cancellationToken)
{
    var query = dbContext.PendingMobileRegistrations
        .Where(item =>
            item.ExpiresAt <= cutoff ||
            item.ConsumedAt <= cutoff);
    return dbContext.Database.IsRelational()
        ? query.ExecuteDeleteAsync(cancellationToken)
        : DeleteTrackedAsync(query, cancellationToken);
}

private async Task<int> DeleteTrackedAsync(
    IQueryable<PendingMobileRegistration> query,
    CancellationToken cancellationToken)
{
    var rows = await query.ToListAsync(cancellationToken);
    dbContext.PendingMobileRegistrations.RemoveRange(rows);
    await dbContext.SaveChangesAsync(cancellationToken);
    return rows.Count;
}
```

Register the repository as scoped in `DependencyInjection.AddInfrastructure`.
The tracked fallback removes matching rows and saves once so the hosted cleanup
worker is testable with the API factory's InMemory provider.

- [ ] **Step 5: Generate and inspect the EF migration**

Run:

```bash
dotnet ef migrations add PhoneFirstRegistration --project src/Toklong.Infrastructure --startup-project src/Toklong.Api --context ToklongDbContext
```

Rename the generated migration basename to
`20260728190000_PhoneFirstRegistration`, and update its `[Migration]` attribute
and designer metadata consistently. Confirm the migration creates only the two
new tables, foreign key to `buyers`, indexes listed above, and no destructive
alteration of existing transaction tables.

- [ ] **Step 6: Run persistence and domain suites**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~PendingRegistrationPersistenceTests
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit persistence**

```bash
git add src/Toklong.Application/Abstractions/IPendingMobileRegistrationRepository.cs src/Toklong.Infrastructure/Persistence/PendingMobileRegistrationRepository.cs src/Toklong.Infrastructure/Persistence/ToklongDbContext.cs src/Toklong.Infrastructure/DependencyInjection.cs src/Toklong.Infrastructure/Persistence/Migrations tests/Toklong.Application.Tests/Authentication/PendingRegistrationPersistenceTests.cs
git commit -m "feat: persist phone registration tickets"
```

### Task 3: Verify-phone and complete-registration application flows

**Files:**
- Create: `src/Toklong.Application/Abstractions/IRegistrationTicketService.cs`
- Create: `src/Toklong.Infrastructure/Security/RegistrationTicketService.cs`
- Modify: `src/Toklong.Infrastructure/DependencyInjection.cs`
- Delete: `src/Toklong.Application/Features/Authentication/CreateMobileSession.cs`
- Create: `src/Toklong.Application/Features/Authentication/VerifyMobileCode.cs`
- Create: `src/Toklong.Application/Features/Authentication/CompleteMobileRegistration.cs`
- Create: `tests/Toklong.Application.Tests/Authentication/PhoneFirstAuthenticationTests.cs`

**Interfaces:**
- Consumes: Task 2 repository and existing `IOtpVerificationProvider`,
  `IBuyerRepository`, `ISellerRepository`, `IUnitOfWork`, and `IClock`.
- Produces:
  `RegistrationTicketPair IRegistrationTicketService.Issue()`.
- Produces:
  `string IRegistrationTicketService.Hash(string rawTicket)`.
- Produces:
  `VerifyMobileCodeCommand(string ChallengeId, string Code, MobileAuthenticationMode Mode, string? InstallationId)`.
- Produces:
  `MobileCodeVerificationResult(MobileSessionProfile? Session, PendingRegistrationResult? Registration)`.
- Produces:
  `CompleteMobileRegistrationCommand(string RegistrationTicket, string FullName, string Email, string TermsVersion, string InstallationId, string IdempotencyKey)`.

- [ ] **Step 1: Write failing verification-flow tests**

```csharp
[Fact]
public async Task New_signup_verification_returns_ticket_without_account()
{
    var result = await Handler.Handle(
        new VerifyMobileCodeCommand(
            ChallengeId,
            "123456",
            MobileAuthenticationMode.SignUp,
            InstallationId),
        default);

    Assert.Null(result.Session);
    Assert.NotNull(result.Registration);
    Assert.Equal(Now.AddMinutes(15), result.Registration.ExpiresAt);
    Assert.Empty(Buyers.Items);
    Assert.Single(Pending.Items);
    Assert.DoesNotContain(
        result.Registration.RegistrationTicket,
        Pending.Items.Single().TicketHash);
}

[Fact]
public async Task Existing_signup_phone_returns_session_profile_after_proof()
{
    Buyers.Items.Add(ExistingBuyer);
    var result = await Handler.Handle(SignUpVerification(), default);
    Assert.Equal(ExistingBuyer.Id, result.Session!.BuyerId);
    Assert.Null(result.Registration);
}
```

- [ ] **Step 2: Run the new application tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~PhoneFirstAuthenticationTests
```

Expected: FAIL because the commands and ticket service do not exist.

- [ ] **Step 3: Implement cryptographic ticket service**

```csharp
public sealed record RegistrationTicketPair(
    string RawTicket,
    string TicketHash);

public RegistrationTicketPair Issue()
{
    var raw = WebEncoders.Base64UrlEncode(
        RandomNumberGenerator.GetBytes(32));
    return new(raw, Hash(raw));
}

public string Hash(string rawTicket) =>
    Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(rawTicket)))
        .ToLowerInvariant();
```

Register this service as singleton. Never log either pair member.

- [ ] **Step 4: Implement `VerifyMobileCodeHandler`**

Move the existing `MobileAuthenticationMode` and `MobileSessionProfile` public
types into `VerifyMobileCode.cs`, delete `CreateMobileSessionCommand` and its
handler, and update API consumers in Task 4. Keep existing sign-in behavior.
For sign-up:

```csharp
if (buyer is not null || seller is not null)
    return MobileCodeVerificationResult.ForSession(
        ProfileFor(buyer, seller, phone));

var pair = tickets.Issue();
var expiresAt = clock.UtcNow.AddMinutes(15);
await pendingRegistrations.AddAsync(
    PendingMobileRegistration.Create(
        pair.TicketHash,
        phone,
        RequiredInstallationId(request.InstallationId),
        clock.UtcNow,
        expiresAt),
    cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
return MobileCodeVerificationResult.ForRegistration(
    pair.RawTicket,
    expiresAt,
    MaskLocalPhone(phone));
```

Remove full name and email from code verification. Preserve the existing error
for a sign-in number with no buyer or seller.

- [ ] **Step 5: Write failing completion and replay tests**

```csharp
[Fact]
public async Task Complete_creates_buyer_acceptance_and_consumes_ticket()
{
    var profile = await CompleteHandler.Handle(ValidCompletion(), default);
    Assert.Equal("+66812345678", profile.PhoneNumber);
    Assert.Single(Buyers.Items);
    Assert.Single(Pending.Acceptances);
    Assert.NotNull(Pending.Items.Single().ConsumedAt);
}

[Fact]
public async Task Exact_completion_retry_returns_same_buyer()
{
    var first = await CompleteHandler.Handle(ValidCompletion(), default);
    var second = await CompleteHandler.Handle(ValidCompletion(), default);
    Assert.Equal(first.BuyerId, second.BuyerId);
    Assert.Single(Buyers.Items);
    Assert.Single(Pending.Acceptances);
}
```

- [ ] **Step 6: Implement completion command**

Hash the raw ticket, load the pending row, call `ValidateCompletion`, and:

- on `ExactReplay`, load the recorded buyer and return its session profile;
- on `Ready`, validate `terms-mvp-v1`, create `BuyerAccount`, create one
  `MobileAccountTermsAcceptance`, mark the pending row complete, and call one
  `SaveChangesAsync`.

Do not require email uniqueness. Use `BuyerAccount.Create` for name and email
validation.

- [ ] **Step 7: Run application authentication tests**

Run:

```bash
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj --filter FullyQualifiedName~PhoneFirstAuthenticationTests
```

Expected: PASS, including expired ticket, installation mismatch, idempotency
mismatch, invalid profile preserving the ticket, and existing-account cases.

- [ ] **Step 8: Commit application flows**

```bash
git add src/Toklong.Application/Abstractions/IRegistrationTicketService.cs src/Toklong.Infrastructure/Security/RegistrationTicketService.cs src/Toklong.Infrastructure/DependencyInjection.cs src/Toklong.Application/Features/Authentication tests/Toklong.Application.Tests/Authentication/PhoneFirstAuthenticationTests.cs
git commit -m "feat: add phone-first registration flow"
```

### Task 4: Mobile authentication API, atomic session issuance, and cleanup

**Files:**
- Modify: `src/Toklong.Api/Program.cs`
- Modify: `src/Toklong.Api/Api/MobileApi.cs`
- Create: `src/Toklong.Api/Services/PendingRegistrationCleanupWorker.cs`
- Modify: `src/Toklong.Api/appsettings.json`
- Modify: `tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs`

**Interfaces:**
- Consumes: Task 3 commands.
- Produces JSON verification outcome
  `session` or `registration_required`.
- Produces `POST /api/mobile/auth/registration/complete`.

- [ ] **Step 1: Rewrite the sign-up integration test to fail on the new contract**

```csharp
using var otp = await client.PostAsJsonAsync(
    "/api/mobile/auth/otp/request",
    new
    {
        PhoneNumber = "0812345678",
        Mode = "SignUp"
    });
var challenge = await Read<OtpResponse>(otp);

using var verified = await client.PostAsJsonAsync(
    "/api/mobile/auth/otp/verify",
    new
    {
        challenge.ChallengeId,
        Code = "123456",
        Mode = "SignUp",
        InstallationId
    });
var registration = await Read<VerificationResponse>(verified);
Assert.Equal("registration_required", registration.Outcome);
Assert.NotEmpty(registration.Registration!.RegistrationTicket);

using var completeRequest = new HttpRequestMessage(
    HttpMethod.Post,
    "/api/mobile/auth/registration/complete")
{
    Content = JsonContent.Create(new
    {
        registration.Registration.RegistrationTicket,
        FullName = "ผู้ซื้อ ทดสอบ",
        Email = "buyer@example.com",
        TermsVersion = "terms-mvp-v1",
        InstallationId
    })
};
completeRequest.Headers.Add("Idempotency-Key", CompletionId);
using var completed = await client.SendAsync(completeRequest);
completed.EnsureSuccessStatusCode();
```

- [ ] **Step 2: Run the API authentication test and verify red**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --filter FullyQualifiedName~MobileAuthenticationApiTests
```

Expected: FAIL because verification still expects profile fields and the
completion route does not exist.

- [ ] **Step 3: Add API DTOs and discriminated verification response**

Retain nullable `FullName` and `Email` compatibility properties on
`MobileOtpRequest` and `MobileOtpVerification` only long enough to return
`400 Bad Request` when an old client supplies either property for sign-up; do
not pass them to any application command. Return:

```csharp
public sealed record MobileOtpVerificationResponse(
    string Outcome,
    MobileSessionResponse? Session,
    MobileRegistrationRequiredResponse? Registration);

public sealed record MobileRegistrationRequiredResponse(
    string RegistrationTicket,
    DateTimeOffset ExpiresAt,
    string MaskedPhoneNumber);
```

Reject non-null compatibility profile fields instead of silently retaining
them.

- [ ] **Step 4: Implement registration completion with an explicit transaction**

In the endpoint, require a normalized GUID `Idempotency-Key`, begin an EF
database transaction when the provider is relational, send
`CompleteMobileRegistrationCommand`, call
`MobileSessionTokenService.CreateAsync`, then commit. In-memory API tests use
the same sequence without opening an unsupported transaction.

Return the existing flat `MobileSessionResponse`. Roll back account,
acceptance, ticket consumption, and session if token issuance fails.

- [ ] **Step 5: Add the completion rate limiter and cleanup worker**

Add `registration-complete` with 10 attempts per 10 minutes and no queue using
the existing privacy-preserving client partition. The cleanup worker runs at
startup and every six hours, deleting rows whose `ExpiresAt` or `ConsumedAt`
is at least 24 hours old. It logs only the deletion count.

- [ ] **Step 6: Add security and replay integration cases**

Add API tests for:

- sign-up request with name/email rejected;
- invalid terms version rejected;
- wrong installation ID rejected;
- same idempotency key returns a session for the same buyer;
- a different idempotency key cannot reuse the ticket;
- existing account verified through sign-up returns `session`;
- registration response and logs never contain the OTP hash or ticket hash.

- [ ] **Step 7: Run API and application tests**

Run:

```bash
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj --filter FullyQualifiedName~MobileAuthenticationApiTests
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit API behavior**

```bash
git add src/Toklong.Api/Program.cs src/Toklong.Api/Api/MobileApi.cs src/Toklong.Api/Services/PendingRegistrationCleanupWorker.cs src/Toklong.Api/appsettings.json tests/Toklong.Api.Tests/Api/MobileAuthenticationApiTests.cs
git commit -m "feat: expose phone-first registration API"
```

### Task 5: Mobile pending-registration and installation services

**Files:**
- Modify: `src/Toklong.Mobile/Core/IAuthenticationService.cs`
- Create: `src/Toklong.Mobile/Core/IPendingRegistrationStore.cs`
- Create: `src/Toklong.Mobile/Core/IInstallationIdProvider.cs`
- Create: `src/Toklong.Mobile/Core/AuthenticationRoutes.cs`
- Create: `src/Toklong.Mobile/Core/InMemoryPendingRegistrationStore.cs`
- Create: `src/Toklong.Mobile/Services/SecurePendingRegistrationStore.cs`
- Create: `src/Toklong.Mobile/Services/InstallationIdProvider.cs`
- Modify: `src/Toklong.Mobile/Services/ApiPushRegistrationClient.cs`
- Modify: `src/Toklong.Mobile/Services/MobileAuthenticationService.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/PendingRegistrationStoreTests.cs`

**Interfaces:**
- Produces:
  `PendingMobileRegistration(string RegistrationTicket, DateTimeOffset ExpiresAt, string MaskedPhoneNumber, string InstallationId, string CompletionIdempotencyKey)`.
- Produces:
  `Task<PendingMobileRegistration?> IPendingRegistrationStore.GetValidAsync(DateTimeOffset now)`.
- Produces:
  `Task SaveAsync(PendingMobileRegistration pending)` and `void Clear()`.
- Produces:
  `string IInstallationIdProvider.GetInstallationId()`.
- Produces:
  `AuthenticationRoutes.CompleteRegistration` with the literal route
  `"CompleteRegistrationPage"`.
- Changes `VerifyCodeAsync` to return
  `AuthenticationVerificationResult`.
- Adds `CompleteRegistrationAsync(string fullName, string email, string termsVersion, CancellationToken cancellationToken)`.

- [ ] **Step 1: Write failing pending-store tests**

```csharp
[Fact]
public async Task GetValidAsync_clears_expired_registration()
{
    var store = new InMemoryPendingRegistrationStore();
    await store.SaveAsync(Pending(ExpiresAt: Now.AddMinutes(-1)));

    Assert.Null(await store.GetValidAsync(Now));
    Assert.Null(await store.GetValidAsync(Now));
}

[Fact]
public async Task Completion_idempotency_key_survives_resume()
{
    var store = new InMemoryPendingRegistrationStore();
    var pending = Pending(CompletionIdempotencyKey: Guid.NewGuid().ToString("N"));
    await store.SaveAsync(pending);
    Assert.Equal(
        pending.CompletionIdempotencyKey,
        (await store.GetValidAsync(Now))!.CompletionIdempotencyKey);
}
```

- [ ] **Step 2: Run mobile core tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~PendingRegistrationStoreTests
```

Expected: FAIL because pending registration types do not exist.

- [ ] **Step 3: Implement core contracts and stores**

The secure store uses separate keys prefixed
`toklong.auth.pending-registration.` and validates all fields before returning.
It creates one completion UUID when saving the API registration result and
reuses that UUID until success, cancellation, or expiry. The DEBUG iOS
simulator uses `InMemoryPendingRegistrationStore`, matching the existing
session-store fallback.

- [ ] **Step 4: Extract one installation ID provider**

Move `ApiPushRegistrationClient.GetInstallationId` to
`InstallationIdProvider` and inject the provider into both push registration
and mobile authentication. Keep the existing preference key
`toklong.notification.installation-id` so current installations retain their
identity.

- [ ] **Step 5: Update `MobileAuthenticationService`**

Request sign-up OTP with phone and mode only. Parse:

```csharp
public abstract record AuthenticationVerificationResult;
public sealed record SessionVerificationResult
    : AuthenticationVerificationResult;
public sealed record RegistrationRequiredVerificationResult(
    PendingMobileRegistration Pending)
    : AuthenticationVerificationResult;
```

Save session responses to `IMobileSessionStore`. Save registration responses to
`IPendingRegistrationStore`. Completion sends the stable idempotency header,
stores the returned session, and clears pending registration only after the
session save succeeds.

- [ ] **Step 6: Run mobile core tests**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit mobile authentication services**

```bash
git add src/Toklong.Mobile/Core/IAuthenticationService.cs src/Toklong.Mobile/Core/IPendingRegistrationStore.cs src/Toklong.Mobile/Core/IInstallationIdProvider.cs src/Toklong.Mobile/Core/AuthenticationRoutes.cs src/Toklong.Mobile/Core/InMemoryPendingRegistrationStore.cs src/Toklong.Mobile/Services/SecurePendingRegistrationStore.cs src/Toklong.Mobile/Services/InstallationIdProvider.cs src/Toklong.Mobile/Services/ApiPushRegistrationClient.cs src/Toklong.Mobile/Services/MobileAuthenticationService.cs src/Toklong.Mobile/MauiProgram.cs tests/Toklong.Mobile.Core.Tests/PendingRegistrationStoreTests.cs
git commit -m "feat: persist pending mobile registration"
```

### Task 6: Centered brand, Thai phone field, Welcome, and sign-in UI

**Files:**
- Create: `src/Toklong.Mobile/Controls/CenteredAuthBrandView.xaml`
- Create: `src/Toklong.Mobile/Controls/CenteredAuthBrandView.xaml.cs`
- Create: `src/Toklong.Mobile/Controls/ThaiMobilePhoneField.xaml`
- Create: `src/Toklong.Mobile/Controls/ThaiMobilePhoneField.xaml.cs`
- Create: `src/Toklong.Mobile/Resources/Images/ui_smartphone.svg`
- Modify: `src/Toklong.Mobile/Pages/WelcomePage.xaml`
- Modify: `src/Toklong.Mobile/Pages/SignInPage.xaml`
- Modify: `src/Toklong.Mobile/ViewModels/SignInViewModel.cs`
- Modify: `src/Toklong.Mobile/App.xaml`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Create: `tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs`

**Interfaces:**
- Consumes: existing `brand_mark.svg`, `ThaiMobilePhoneEntry`, and Task 5
  authentication service.
- Produces reusable centered brand and local Thai-phone field controls.

- [ ] **Step 1: Write failing static UI tests**

```csharp
[Fact]
public void Welcome_uses_centered_brand_and_removes_old_hero_art()
{
    var xaml = ReadPage("WelcomePage.xaml");
    Assert.Contains("CenteredAuthBrandView", xaml);
    Assert.Contains("ซื้อขายออนไลน์ ง่ายขึ้น", xaml);
    Assert.DoesNotContain("ui_shield", xaml);
    Assert.DoesNotContain("ui_truck", xaml);
}

[Fact]
public void SignIn_is_explicitly_phone_and_sms_first()
{
    var xaml = ReadPage("SignInPage.xaml");
    Assert.Contains("เข้าสู่ระบบด้วยเบอร์มือถือ", xaml);
    Assert.Contains("ThaiMobilePhoneField", xaml);
    Assert.Contains("ส่งรหัสทาง SMS", xaml);
    Assert.DoesNotContain("+66", xaml);
}
```

- [ ] **Step 2: Run layout tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~AuthenticationLayoutTests
```

Expected: FAIL against the current Welcome and SignIn XAML.

- [ ] **Step 3: Implement exact centered brand**

Compose the existing `brand_mark.png` inside the existing
`#3C8AF1 → #216ACB → #5D43C4` rounded tile. Expose one
`SemanticProperties.Description="โลโก้ TOKLONG"` on the container and exclude
the image and visible wordmark from separate semantics.

- [ ] **Step 4: Implement Thai phone field with the selected modern icon**

Use `ui_smartphone.svg`, `ThaiMobilePhoneEntry`, label
`เบอร์มือถือไทย`, and helper `กรอกเบอร์ 10 หลัก เช่น 081-234-5678`.
Do not render `+66`, country flags, or a picker. Keep the icon decorative.

- [ ] **Step 5: Redesign Welcome and sign-in**

Welcome order is centered brand, approved benefit copy, full-width
`เข้าสู่ระบบ`, text-weight `สมัครสมาชิก`. Sign-in uses the smaller centered
brand, explicit SMS copy, shared phone field, progress state, passwordless
reassurance, and existing sign-up route. Guard navigation with the existing
`IsBusy` state.

- [ ] **Step 6: Run UI tests and compile mobile XAML**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~AuthenticationLayoutTests
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj -t:Compile -p:TargetFrameworks=net10.0-ios -p:TargetFramework=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -p:NuGetAudit=false
```

Expected: PASS with XAML source generation succeeding.

- [ ] **Step 7: Commit Welcome and sign-in UI**

```bash
git add src/Toklong.Mobile/Controls/CenteredAuthBrandView.xaml src/Toklong.Mobile/Controls/CenteredAuthBrandView.xaml.cs src/Toklong.Mobile/Controls/ThaiMobilePhoneField.xaml src/Toklong.Mobile/Controls/ThaiMobilePhoneField.xaml.cs src/Toklong.Mobile/Resources/Images/ui_smartphone.svg src/Toklong.Mobile/Pages/WelcomePage.xaml src/Toklong.Mobile/Pages/SignInPage.xaml src/Toklong.Mobile/ViewModels/SignInViewModel.cs src/Toklong.Mobile/App.xaml src/Toklong.Mobile/MauiProgram.cs tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs
git commit -m "feat: redesign welcome and phone sign in"
```

### Task 7: Phone-first sign-up, mode-aware verification, and profile completion UI

**Files:**
- Modify: `src/Toklong.Mobile/Pages/SignUpPage.xaml`
- Modify: `src/Toklong.Mobile/Pages/SignUpPage.xaml.cs`
- Modify: `src/Toklong.Mobile/ViewModels/SignUpViewModel.cs`
- Modify: `src/Toklong.Mobile/Pages/VerifyCodePage.xaml`
- Modify: `src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs`
- Create: `src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml`
- Create: `src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml.cs`
- Create: `src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs`
- Modify: `src/Toklong.Mobile/AppShell.xaml.cs`
- Modify: `src/Toklong.Mobile/MauiProgram.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs`

**Interfaces:**
- Consumes: Task 5 verification result and pending-registration service.
- Produces global Shell route `CompleteRegistrationPage`.

- [ ] **Step 1: Add failing sign-up, OTP, and completion layout tests**

```csharp
[Fact]
public void SignUp_collects_phone_only_before_sms()
{
    var xaml = ReadPage("SignUpPage.xaml");
    Assert.Contains("สมัครด้วยเบอร์มือถือ", xaml);
    Assert.Contains("ThaiMobilePhoneField", xaml);
    Assert.DoesNotContain("ชื่อและนามสกุล", xaml);
    Assert.DoesNotContain("อีเมล", xaml);
}

[Fact]
public void CompleteRegistration_collects_profile_after_verified_phone()
{
    var xaml = ReadPage("CompleteRegistrationPage.xaml");
    Assert.Contains("ตั้งค่าบัญชีให้เสร็จ", xaml);
    Assert.Contains("ชื่อและนามสกุล", xaml);
    Assert.Contains("อีเมลสำหรับใบเสร็จและการคืนเงิน", xaml);
    Assert.Contains("สร้างบัญชีและเริ่มใช้งาน", xaml);
}
```

- [ ] **Step 2: Run layout tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~AuthenticationLayoutTests
```

Expected: FAIL because SignUp still collects profile and completion page is
absent.

- [ ] **Step 3: Convert SignUp to phone-only**

Remove full name/email state and validation from `SignUpViewModel`. Request an
OTP with `AuthenticationMode.SignUp`. Use approved copy and navigate to
`VerifyCodePage` with only challenge, masked number, local number, mode, and
development code.

- [ ] **Step 4: Make verification outcome-aware**

`VerifyCodeViewModel.ConfirmAsync` handles:

```csharp
switch (await authentication.VerifyCodeAsync(...))
{
    case SessionVerificationResult:
        await Shell.Current.GoToAsync("//transactions");
        break;
    case RegistrationRequiredVerificationResult:
        await Shell.Current.GoToAsync(
            AuthenticationRoutes.CompleteRegistration);
        break;
}
```

Use `ยืนยันและเข้าสู่ระบบ` for sign-in and `ยืนยันเบอร์มือถือ` for sign-up.
Add `แก้ไขเบอร์`, preserve one focusable OTP input, and keep the resend
countdown/API cooldown behavior.

- [ ] **Step 5: Implement profile completion**

The ViewModel validates name and email locally, shows the verified masked phone
read-only, sends terms version `terms-mvp-v1`, opens Terms and Privacy URLs with
`Launcher.Default`, disables duplicate submission, and routes to transactions
only after session persistence succeeds.

The page uses tappable underlined `ข้อตกลงการใช้บริการ` and
`นโยบายความเป็นส่วนตัว` text immediately before the primary button. It does
not add a checkbox because pressing the explicitly labelled completion button
records acceptance; the full sentence must state that effect.

- [ ] **Step 6: Run layout tests and compile**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~AuthenticationLayoutTests
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj -t:Compile -p:TargetFrameworks=net10.0-ios -p:TargetFramework=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -p:NuGetAudit=false
```

Expected: PASS.

- [ ] **Step 7: Commit phone-first sign-up UI**

```bash
git add src/Toklong.Mobile/Pages/SignUpPage.xaml src/Toklong.Mobile/Pages/SignUpPage.xaml.cs src/Toklong.Mobile/ViewModels/SignUpViewModel.cs src/Toklong.Mobile/Pages/VerifyCodePage.xaml src/Toklong.Mobile/ViewModels/VerifyCodeViewModel.cs src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml src/Toklong.Mobile/Pages/CompleteRegistrationPage.xaml.cs src/Toklong.Mobile/ViewModels/CompleteRegistrationViewModel.cs src/Toklong.Mobile/AppShell.xaml.cs src/Toklong.Mobile/MauiProgram.cs tests/Toklong.Mobile.Core.Tests/AuthenticationLayoutTests.cs
git commit -m "feat: add phone-first mobile sign up"
```

### Task 8: Startup resume, documentation, and full verification

**Files:**
- Modify: `src/Toklong.Mobile/Core/StartupCoordinator.cs`
- Modify: `src/Toklong.Mobile/App.xaml.cs`
- Modify: `tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs`
- Modify: `docs/02_UI_UX_AND_CONTENT_SPEC.md`
- Modify: `docs/05_ACCEPTANCE_TESTS.md`

**Interfaces:**
- Consumes: Task 5 `IPendingRegistrationStore`.
- Produces startup priority: valid session → transactions; valid pending
  registration → completion; otherwise Welcome.

- [ ] **Step 1: Write failing startup routing tests**

```csharp
[Fact]
public async Task StartAsync_without_session_with_pending_registration_routes_to_completion()
{
    var coordinator = new StartupCoordinator(
        Authentication(hasSession: false),
        Pending(valid: true),
        Motion(reduced: false));

    var result = await coordinator.StartAsync(_ => Task.CompletedTask);

    Assert.Equal(AuthenticationRoutes.CompleteRegistration, result.Route);
}

[Fact]
public async Task StartAsync_prefers_authenticated_session_over_pending_registration()
{
    var coordinator = new StartupCoordinator(
        Authentication(hasSession: true),
        Pending(valid: true),
        Motion(reduced: false));
    Assert.Equal(
        "//transactions",
        (await coordinator.StartAsync(_ => Task.CompletedTask)).Route);
}
```

- [ ] **Step 2: Run startup tests and verify red**

Run:

```bash
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj --filter FullyQualifiedName~StartupCoordinatorTests
```

Expected: FAIL because startup does not inspect pending registration.

- [ ] **Step 3: Implement pending-registration startup routing**

Resolve session and pending registration concurrently with startup motion.
Session wins if both are present. A pending-store error is logged and falls
back to Welcome without exposing ticket data. `App` installs `AppShell` once,
navigates to completion, and does not initialize authenticated push/deep-link
services until a real session exists.

- [ ] **Step 4: Update product and acceptance documentation**

Record the approved Welcome copy, local phone field, selected smartphone icon,
three-step sign-up, 15-minute ticket, exact startup resume behavior, and
security/accessibility acceptance cases. Do not describe the registration
ticket, hash, or idempotency key in consumer copy.

- [ ] **Step 5: Run all affected test projects separately**

Run:

```bash
dotnet test tests/Toklong.Domain.Tests/Toklong.Domain.Tests.csproj
dotnet test tests/Toklong.Application.Tests/Toklong.Application.Tests.csproj
dotnet test tests/Toklong.Api.Tests/Toklong.Api.Tests.csproj
dotnet test tests/Toklong.Mobile.Core.Tests/Toklong.Mobile.Core.Tests.csproj
dotnet test tests/Toklong.Crm.Tests/Toklong.Crm.Tests.csproj
```

Expected: all tests PASS. Run separately because the full solution currently
requires an unavailable Android workload.

- [ ] **Step 6: Build iOS compile and simulator package**

Run compile:

```bash
dotnet msbuild src/Toklong.Mobile/Toklong.Mobile.csproj -t:Compile -p:TargetFrameworks=net10.0-ios -p:TargetFramework=net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 -p:NuGetAudit=false
```

Then run the full package outside the restricted sandbox so the arm64 ILLink
task host can start:

```bash
dotnet build src/Toklong.Mobile/Toklong.Mobile.csproj -p:TargetFrameworks=net10.0-ios -f net10.0-ios -r iossimulator-arm64 --no-restore -p:NuGetAudit=false
```

Expected: both commands succeed. Install on the booted iPhone simulator and
manually check Welcome, sign-in, phone-first sign-up, OTP, completion,
foreground/background resume during completion, large text, VoiceOver,
invalid/expired code, and existing-account sign-up. Verify cold-process resume
on a signed physical iOS build because the repository's unsigned iOS simulator
configuration deliberately uses in-memory auth stores when Keychain
persistence is unavailable.

- [ ] **Step 7: Run repository hygiene checks**

Run:

```bash
git diff --check
rg -n 'OTP|registration ticket|ticket hash|idempotency' src/Toklong.Mobile/Pages -g '*.xaml'
rg -n '081-234-5678|เบอร์มือถือไทย|ส่งรหัสทาง SMS' src/Toklong.Mobile/Pages src/Toklong.Mobile/Controls -g '*.xaml'
```

Expected: no diff whitespace errors; no internal security terms in consumer
XAML; approved Thai phone copy present.

- [ ] **Step 8: Commit startup and documentation**

```bash
git add src/Toklong.Mobile/Core/StartupCoordinator.cs src/Toklong.Mobile/App.xaml.cs tests/Toklong.Mobile.Core.Tests/StartupCoordinatorTests.cs docs/02_UI_UX_AND_CONTENT_SPEC.md docs/05_ACCEPTANCE_TESTS.md
git commit -m "feat: resume phone registration on startup"
```

- [ ] **Step 9: Final branch review**

Run:

```bash
git status --short
git log --oneline --decorate -12
```

Confirm no generated secrets, raw OTPs, raw registration tickets, personal
data, provider keys, or unrelated files are staged. Report the legal Terms and
Privacy page deployment dependency before release.
