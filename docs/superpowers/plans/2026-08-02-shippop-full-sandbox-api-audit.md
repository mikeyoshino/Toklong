# SHIPPOP Full Sandbox API Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and run an opt-in, sanitized certification that calls every SHIPPOP endpoint used by TOKLONG against one synthetic Sandbox shipment and cancels it exactly once.

**Architecture:** Keep the consumer application and transaction database out of the exercise. A provider-neutral lifecycle harness calls the production `ShippopShippingProvider` through `IShippingQuoteProvider` and `IShipmentProvider`; a guarded runtime context supplies Sandbox-only configuration, and a separate reporter exposes only allow-listed outcomes. Prove mutation and cleanup behavior with deterministic fakes before enabling the live test.

**Tech Stack:** .NET 10, C#, xUnit 2.9, `HttpClient`, `System.Text.Json`, Bash

## Global Constraints

- Use only `https://mkpservice.shippop.dev`; reject HTTP, Production, lookalike hosts, explicit ports, user info, paths, queries, and fragments before reading credentials or synthetic personal data.
- Require exact `SHIPPOP_CERTIFY=1` and `SHIPPOP_CERTIFY_MUTATIONS=1` for the live lifecycle.
- Call `pricelist/`, `booking/`, `confirm/`, `label/`, `tracking/`, and `cancel/` through the current production provider.
- Create at most one synthetic outbound shipment per run.
- Never retry `booking/`, `confirm/`, or `cancel/` after a failed or unknown outcome.
- Attempt `cancel/` exactly once only after confirmation returns an authoritative courier tracking identifier.
- If confirmation may have succeeded but no safe cleanup identifier exists, return `cleanup_required` and do not rerun automatically.
- Require the fixture marker `certificationFixture: true` and test-only contact/parcel markers before reading credentials.
- Never report API keys, account email, contacts, addresses, phone numbers, provider IDs, tracking numbers, raw bodies, label HTML, or barcode/QR content.
- Do not create a TOKLONG transaction or affect payment, dispute, refund, delivery, or payout state.
- Keep the live test skipped during normal test runs.
- Do not modify or merge the separate counter-QR worktree.

## File Map

- Create `tests/Toklong.Shippop.Certification/CertificationEndpointGuard.cs`: exact HTTPS endpoint allow-list.
- Create `tests/Toklong.Shippop.Certification/FullLifecycleCertificationHarness.cs`: ordered lifecycle, blocking, and cleanup.
- Create `tests/Toklong.Shippop.Certification/FullLifecycleCertificationReport.cs`: sanitized allow-listed report.
- Create `tests/Toklong.Shippop.Certification/FullLifecycleCertificationContext.cs`: environment/fixture validation and real provider construction.
- Create `tests/Toklong.Shippop.Certification/FullLifecycleCertificationTests.cs`: offline unit tests and one opt-in live fact.
- Modify `tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs`: replace obsolete nested HTTP guard.
- Modify `scripts/shippop-certify.sh`: add `full-lifecycle` mode.
- Create `tests/scripts/shippop-certify-tests.sh`: test runner preflight without network calls.
- Modify `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`: replace obsolete HTTP guidance and document lifecycle operation.

---

### Task 1: Require the exact HTTPS Sandbox endpoint

**Files:**
- Create: `tests/Toklong.Shippop.Certification/CertificationEndpointGuard.cs`
- Modify: `tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs:409-438,529-533,1253-1270`

**Interfaces:**
- Produces: `CertificationEndpointGuard.EnsureApproved(string baseUrl) : void`.
- Consumes: literal origin `https://mkpservice.shippop.dev`.

- [ ] **Step 1: Replace the old guard tests with failing HTTPS tests**

```csharp
[Theory]
[InlineData("https://mkpservice.shippop.dev")]
[InlineData("https://mkpservice.shippop.dev/")]
public void Certification_endpoint_allows_only_approved_https(string baseUrl) =>
    CertificationEndpointGuard.EnsureApproved(baseUrl);

[Theory]
[InlineData("http://mkpservice.shippop.dev")]
[InlineData("https://mkpservice.shippop.com")]
[InlineData("https://mkpservice.shippop.dev:443")]
[InlineData("https://user@mkpservice.shippop.dev")]
[InlineData("https://mkpservice.shippop.dev/booking")]
[InlineData("https://mkpservice.shippop.dev?trace=1")]
[InlineData("https://mkpservice.shippop.dev.evil.test")]
public void Certification_endpoint_rejects_unapproved_urls(string baseUrl) =>
    Assert.Throws<InvalidOperationException>(() =>
        CertificationEndpointGuard.EnsureApproved(baseUrl));
```

- [ ] **Step 2: Run the guard tests and observe RED**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~Certification_endpoint --no-restore`

Expected: FAIL because the current nested guard accepts insecure HTTP and rejects HTTPS.

- [ ] **Step 3: Implement the minimal shared guard**

```csharp
namespace Toklong.Shippop.Certification;

internal static class CertificationEndpointGuard
{
    private const string Approved = "https://mkpservice.shippop.dev";

    public static void EnsureApproved(string baseUrl)
    {
        var clean = baseUrl.Trim();
        if (clean.EndsWith('/', StringComparison.Ordinal))
            clean = clean[..^1];
        if (!string.Equals(clean, Approved, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SHIPPOP certification endpoint is not approved.");
    }
}
```

Update `CertificationContext.LoadAsync` to call the one-argument method, force
`allowInsecureHttp: false`, stop reading `SHIPPOP_ALLOW_INSECURE_HTTP`, and
delete the nested guard.

- [ ] **Step 4: Run GREEN and regression tests**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --no-restore`

Expected: offline tests PASS; live facts SKIP without opt-in.

- [ ] **Step 5: Commit**

```bash
git add tests/Toklong.Shippop.Certification/CertificationEndpointGuard.cs tests/Toklong.Shippop.Certification/ShippopServiceCertificationTests.cs
git commit -m "test: require HTTPS for SHIPPOP certification"
```

### Task 2: Implement the deterministic six-endpoint harness

**Files:**
- Create: `tests/Toklong.Shippop.Certification/FullLifecycleCertificationHarness.cs`
- Create: `tests/Toklong.Shippop.Certification/FullLifecycleCertificationTests.cs`

**Interfaces:**
- Consumes: `IShippingQuoteProvider` and `IShipmentProvider`.
- Produces: `RunAsync(ShippingQuoteRequest shipment, string serviceCode, bool mutationsEnabled, CancellationToken cancellationToken) : Task<FullLifecycleCertificationResult>`.
- Produces: six `FullLifecycleCheck` rows and `FullLifecycleCertificationResult.Passed`.

- [ ] **Step 1: Write the successful lifecycle test with a recording fake**

```csharp
[Fact]
public async Task Full_lifecycle_calls_each_endpoint_once_and_cancels_last()
{
    var provider = new RecordingShipmentProvider();
    var result = await new FullLifecycleCertificationHarness(provider, provider)
        .RunAsync(SyntheticShipment(), "EMST", true, default);

    Assert.True(result.Passed);
    Assert.Equal(
        ["pricelist", "booking", "confirm", "label", "tracking", "cancel"],
        provider.Calls);
    Assert.All(result.Checks, check =>
        Assert.Equal(FullLifecycleOutcome.Pass, check.Outcome));
}
```

The fake returns a positive `FeeSatang`, matching `EMST`/`THAIPOST` values,
non-empty purchase/provider/courier references, bounded HTML containing
`<html`, and tracking with `OccurredAt = null` and no delivered event.

- [ ] **Step 2: Run the test and observe RED**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~Full_lifecycle_calls_each_endpoint_once_and_cancels_last --no-restore`

Expected: FAIL because the harness types do not exist.

- [ ] **Step 3: Define the exact result contract**

```csharp
internal enum FullLifecycleOutcome { Pass, Fail, Blocked, CleanupRequired }

internal sealed record FullLifecycleCheck(
    string Capability,
    FullLifecycleOutcome Outcome,
    string ReasonCode);

internal sealed record FullLifecycleCertificationResult(
    IReadOnlyList<FullLifecycleCheck> Checks)
{
    private static readonly string[] Required =
        ["pricelist", "booking", "confirm", "label", "tracking", "cancel"];

    public bool Passed => Required.All(capability =>
        Checks.Single(row => row.Capability == capability).Outcome ==
            FullLifecycleOutcome.Pass);
}
```

- [ ] **Step 4: Implement the ordered success path**

Select exactly one matching positive quote. Call `ReserveAsync` with one unique
`certification-{Guid.NewGuid():N}` operation reference. Confirm using the exact
purchase/provider tracking/carrier/service values. Build `ShipmentLabelRequest`
from the reservation, selected service name, confirmed courier tracking, and
synthetic contacts. Query tracking using provider tracking and carrier. In the
single cleanup boundary call:

```csharp
await shipmentProvider.CancelServiceAsync(
    confirmation.CourierTrackingCode,
    reservation.ServiceCode,
    isReturn: false,
    cancellationToken);
```

Validate every provider/carrier/service/tracking relationship before setting a
row to `Pass`. Store no returned identifiers in a check or exception message.

- [ ] **Step 5: Add failure/no-retry tests**

```csharp
[Fact]
public async Task Mutation_gate_blocks_before_booking()
{
    var provider = new RecordingShipmentProvider();
    var result = await Harness(provider).RunAsync(
        SyntheticShipment(), "EMST", false, default);
    Assert.Equal(["pricelist"], provider.Calls);
    Assert.Equal(FullLifecycleOutcome.Blocked,
        Row(result, "booking").Outcome);
}

[Fact]
public async Task Unknown_booking_is_not_retried_or_cancelled()
{
    var provider = new RecordingShipmentProvider(failAt: "booking");
    var result = await Harness(provider).RunAsync(
        SyntheticShipment(), "EMST", true, default);
    Assert.Equal(1, provider.Calls.Count(x => x == "booking"));
    Assert.DoesNotContain("confirm", provider.Calls);
    Assert.DoesNotContain("cancel", provider.Calls);
    Assert.False(result.Passed);
}

[Theory]
[InlineData("label")]
[InlineData("tracking")]
public async Task Read_failure_still_cancels_once(string failAt)
{
    var provider = new RecordingShipmentProvider(failAt);
    var result = await Harness(provider).RunAsync(
        SyntheticShipment(), "EMST", true, default);
    Assert.Equal(1, provider.Calls.Count(x => x == "cancel"));
    Assert.False(result.Passed);
}

[Fact]
public async Task Unknown_cancel_is_called_once_and_requires_cleanup()
{
    var provider = new RecordingShipmentProvider(failAt: "cancel");
    var result = await Harness(provider).RunAsync(
        SyntheticShipment(), "EMST", true, default);
    Assert.Equal(1, provider.Calls.Count(x => x == "cancel"));
    Assert.Equal(FullLifecycleOutcome.CleanupRequired,
        Row(result, "cancel").Outcome);
}
```

Also cover confirmation failure without a safe courier tracking identifier:
label/tracking become `Blocked`, cancel becomes `CleanupRequired`, and cancel is
not called. Assert booking, confirmation, and cancellation counters never
exceed one in every failure case.

- [ ] **Step 6: Run failure tests and observe RED**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~FullLifecycle --no-restore`

Expected: cleanup/blocking tests FAIL until the one-shot failure paths exist.

- [ ] **Step 7: Implement failure mapping and one-shot cleanup**

Initialize all six rows as `Blocked`. Quote and booking stop dependent calls.
After a valid confirmation, attempt label and tracking independently so one
read failure does not hide the other. Put cancellation in `finally`; call it
only when both authoritative cleanup variables are present. Use only these
fixed reasons:

```text
not_reached, mutation_disabled, quote_missing, quote_price_invalid,
booking_failed, booking_contract_invalid, confirm_failed,
confirm_contract_invalid, label_failed, label_contract_invalid,
tracking_failed, tracking_contract_invalid, cancel_confirmed,
cleanup_required
```

Catch provider exceptions without retaining `Message` or `ToString()`. Never
loop, recurse, or use a retry policy around mutations.

- [ ] **Step 8: Run GREEN and commit**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~FullLifecycle --no-restore`

```bash
git add tests/Toklong.Shippop.Certification/FullLifecycleCertificationHarness.cs tests/Toklong.Shippop.Certification/FullLifecycleCertificationTests.cs
git commit -m "test: add SHIPPOP full lifecycle harness"
```

### Task 3: Add sanitized reporting and the guarded live context

**Files:**
- Create: `tests/Toklong.Shippop.Certification/FullLifecycleCertificationReport.cs`
- Create: `tests/Toklong.Shippop.Certification/FullLifecycleCertificationContext.cs`
- Modify: `tests/Toklong.Shippop.Certification/FullLifecycleCertificationTests.cs`

**Interfaces:**
- Produces: `FullLifecycleCertificationReport.Serialize(result, recordedAtUtc) : string`.
- Produces: `FullLifecycleCertificationContext.LoadAsync()`, `CreateProvider()`, `Shipment`, and `ServiceCode`.

- [ ] **Step 1: Write report allow-list and leakage tests**

```csharp
[Fact]
public void Report_rejects_non_allow_listed_reason()
{
    var result = new FullLifecycleCertificationResult(
        [new("pricelist", FullLifecycleOutcome.Fail, "raw provider body")]);
    Assert.Throws<InvalidOperationException>(() =>
        FullLifecycleCertificationReport.Serialize(
            result, DateTimeOffset.UnixEpoch));
}

[Fact]
public void Report_contains_no_provider_artifacts()
{
    var json = FullLifecycleCertificationReport.Serialize(
        ResultWithAllPasses(), DateTimeOffset.UnixEpoch);
    Assert.Contains("\"capability\": \"pricelist\"", json);
    Assert.DoesNotContain("purchase-test", json, StringComparison.Ordinal);
    Assert.DoesNotContain("courier-track-test", json, StringComparison.Ordinal);
    Assert.DoesNotContain("<html", json, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run report tests and observe RED**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~Report_ --no-restore`

- [ ] **Step 3: Implement the allow-listed report**

Allow exactly the six capabilities, four outcomes, and fixed reason codes from
Task 2. Serialize only:

```csharp
internal sealed record SanitizedLifecycleDocument(
    string Environment,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<SanitizedLifecycleRow> Checks,
    bool Passed);

internal sealed record SanitizedLifecycleRow(
    string Capability,
    string Outcome,
    string ReasonCode);
```

Set `Environment` to literal `shippop-sandbox`. Do not accept it from runtime
input and do not serialize exception text or provider objects.

- [ ] **Step 4: Write context gate tests**

Use a disposable environment-variable scope and disable xUnit parallelization
for these tests. Verify gate order: `SHIPPOP_CERTIFY`, mutations, endpoint,
service code, absolute synthetic fixture, validated dimensions/test markers,
then API key and account email. The valid fixture must contain
`"certificationFixture": true`, origin/destination names containing
`TOKLONG TEST`, both phone numbers equal to `0000000000`, and parcel name
`TOKLONG TEST PARCEL`. Include:

```csharp
[Fact]
public async Task Context_requires_mutation_gate_before_credentials()
{
    using var environment = CertificationEnvironment.Valid();
    environment.Set("SHIPPOP_CERTIFY_MUTATIONS", "0");
    environment.Set("SHIPPOP_API_KEY", "forbidden-marker-api-key");
    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        FullLifecycleCertificationContext.LoadAsync);
    Assert.Equal("SHIPPOP mutation certification is not enabled.", error.Message);
    Assert.DoesNotContain("forbidden-marker", error.ToString());
}
```

- [ ] **Step 5: Run context tests and observe RED**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~Context_ --no-restore`

- [ ] **Step 6: Implement guarded loading and provider construction**

Construct one enabled Sandbox-only profile:

```csharp
new ShippopServiceProfile(
    serviceCode,
    QuoteEnabled: true,
    BookOutboundEnabled: true,
    ConfirmEnabled: true,
    ReturnEnabled: false,
    InsuranceEnabled: false,
    OperationLookupEnabled: true,
    HandoffMode: "DropOff",
    MaximumCoverageSatang: 0,
    CertificationReference: "sandbox-full-lifecycle-audit");
```

Reject a false/missing fixture marker, non-test contact name, different phone,
or different parcel name before credentials are read. Use
`AllowInsecureHttp = false`, one `ServiceCodes` entry, one matching
`ServiceProfiles` entry, signing secret
`certification-only-signing-secret-32-characters`, and a 30-second
`HttpClient.Timeout`. Validate positive weight/dimensions and positive declared
value in integer satang.

- [ ] **Step 7: Add the opt-in live fact**

Create `[FullLifecycleCertificationFact]` that skips unless both gates are
exactly `1`, then add:

```csharp
[FullLifecycleCertificationFact]
[Trait("Category", "ShippopFullLifecycle")]
public async Task Full_lifecycle_calls_every_current_endpoint_and_cleans_up()
{
    var context = await FullLifecycleCertificationContext.LoadAsync();
    var provider = context.CreateProvider();
    var result = await new FullLifecycleCertificationHarness(provider, provider)
        .RunAsync(context.Shipment, context.ServiceCode, true, default);
    Console.WriteLine(FullLifecycleCertificationReport.Serialize(
        result, DateTimeOffset.UtcNow));
    Assert.True(result.Passed,
        "SHIPPOP Sandbox lifecycle did not pass; inspect sanitized rows only.");
}
```

- [ ] **Step 8: Run offline GREEN and commit**

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --no-restore`

Expected: offline tests PASS; live fact SKIPS.

```bash
git add tests/Toklong.Shippop.Certification/FullLifecycleCertificationReport.cs tests/Toklong.Shippop.Certification/FullLifecycleCertificationContext.cs tests/Toklong.Shippop.Certification/FullLifecycleCertificationTests.cs
git commit -m "test: guard SHIPPOP sandbox lifecycle audit"
```

### Task 4: Add the runner, runbook, and live execution checkpoint

**Files:**
- Modify: `scripts/shippop-certify.sh`
- Create: `tests/scripts/shippop-certify-tests.sh`
- Modify: `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`

**Interfaces:**
- Produces: `./scripts/shippop-certify.sh parcel-protection` and `./scripts/shippop-certify.sh full-lifecycle`.
- Consumes: existing `SHIPPOP_*` variables without echoing values.

- [ ] **Step 1: Write shell runner tests with a stubbed dotnet**

Test these functions independently:

```text
full_lifecycle_rejects_http_before_dotnet
full_lifecycle_requires_mutation_gate
full_lifecycle_selects_only_live_lifecycle_fact
failure_output_does_not_contain_fake_secrets
parcel_protection_keeps_existing_mode
```

The stub records only safe argument names and gate flags—never fake key/email
values. Assert the lifecycle filter contains the exact live fact name.

- [ ] **Step 2: Run shell tests and observe RED**

Run: `bash tests/scripts/shippop-certify-tests.sh`

Expected: FAIL because `full-lifecycle` is not supported.

- [ ] **Step 3: Implement exact runner preflight**

```bash
mode="${1:-parcel-protection}"
case "${mode}" in
  parcel-protection|full-lifecycle) ;;
  *) echo "Usage: ./scripts/shippop-certify.sh [parcel-protection|full-lifecycle]" >&2; exit 2 ;;
esac

if [[ "${SHIPPOP_BASE_URL:-}" != "https://mkpservice.shippop.dev" && "${SHIPPOP_BASE_URL:-}" != "https://mkpservice.shippop.dev/" ]]; then
  echo "SHIPPOP_BASE_URL must be the approved HTTPS Sandbox endpoint." >&2
  exit 2
fi

if [[ "${mode}" == "full-lifecycle" && "${SHIPPOP_CERTIFY_MUTATIONS:-}" != "1" ]]; then
  echo "Set SHIPPOP_CERTIFY_MUTATIONS=1 for the synthetic lifecycle." >&2
  exit 2
fi
```

Select filters without `eval`: use
`Protection_quote_and_booking_preserve_exact_values` for the existing mode and
`Full_lifecycle_calls_every_current_endpoint_and_cleans_up` for lifecycle mode.
Export `SHIPPOP_CERTIFY=1`; never echo secrets.

- [ ] **Step 4: Update the runbook**

Replace obsolete HTTP instructions with:

```bash
SHIPPOP_BASE_URL=https://mkpservice.shippop.dev SHIPPOP_API_KEY="$SHIPPOP_API_KEY" SHIPPOP_ACCOUNT_EMAIL="$SHIPPOP_ACCOUNT_EMAIL" SHIPPOP_SERVICE_CODE=EMST SHIPPOP_SYNTHETIC_ADDRESS_JSON=/absolute/path/synthetic-address.json SHIPPOP_CERTIFY_MUTATIONS=1 ./scripts/shippop-certify.sh full-lifecycle
```

Document six rows, four outcomes, possible pre-scan tracking, and the rule:
after `cleanup_required`, stop and obtain operator/provider review before any
rerun. State that a pass is Sandbox account evidence only and cannot enable
Production or payout.

- [ ] **Step 5: Run GREEN and commit**

Run: `bash -n scripts/shippop-certify.sh`

Run: `bash tests/scripts/shippop-certify-tests.sh`

Run: `dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --no-restore`

```bash
git add scripts/shippop-certify.sh tests/scripts/shippop-certify-tests.sh docs/SHIPPOP_CERTIFICATION_RUNBOOK.md
git commit -m "docs: add SHIPPOP full lifecycle audit runner"
```

- [ ] **Step 6: Run repository verification before live mutation**

Run: `dotnet format Toklong.slnx --verify-no-changes`

Run: `dotnet test Toklong.slnx --no-restore`

Run: `bash tests/scripts/local-shipping-mode-tests.sh`

Run: `bash tests/scripts/shippop-certify-tests.sh`

Run: `git diff --check`

Expected: all offline checks PASS and live facts SKIP.

- [ ] **Step 7: Confirm inputs without printing values and run once**

Presence checks:

```bash
test -n "${SHIPPOP_API_KEY:-}"
test -n "${SHIPPOP_ACCOUNT_EMAIL:-}"
test -f "${SHIPPOP_SYNTHETIC_ADDRESS_JSON:?missing synthetic fixture path}"
```

Do not use `env`, `set`, `printenv`, or shell tracing. Then run exactly once:

```bash
SHIPPOP_BASE_URL=https://mkpservice.shippop.dev SHIPPOP_CERTIFY_MUTATIONS=1 ./scripts/shippop-certify.sh full-lifecycle
```

Expected success: six sanitized `pass` rows and exit code 0. On
`cleanup_required`, stop immediately; do not rerun or make a manual mutation.

- [ ] **Step 8: Final verification and completion evidence**

Run: `dotnet test Toklong.slnx --no-restore`

Run: `bash tests/scripts/local-shipping-mode-tests.sh`

Run: `bash tests/scripts/shippop-certify-tests.sh`

Run: `git status --short --branch`

Report only endpoint status/reason rows and cleanup outcome. Do not include
fixture content, credentials, provider references, tracking values, label HTML,
or raw provider responses.
