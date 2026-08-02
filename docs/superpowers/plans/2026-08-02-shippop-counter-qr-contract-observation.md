# SHIPPOP Counter QR Contract Observation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and run a secret-safe SHIPPOP Dev observation harness that proves whether the existing unconfirmed-booking or confirmation response exposes a counter-QR candidate, without enabling a production service or guessing a read endpoint.

**Architecture:** A certification-only `DelegatingHandler` observes the existing `booking/` and `confirm/` responses in memory, converts them immediately into a bounded structure containing only safe field paths and JSON kinds, and restores the response for the unchanged production adapter. A live opt-in test executes one synthetic quote → booking → confirmation → cancellation lifecycle and writes only the sanitized observation outside the repository. This phase stops before application QR implementation because that plan requires the official artifact contract and a documented safe read/retry path.

**Tech Stack:** .NET 10, C# 14, `HttpClient`, `System.Text.Json`, xUnit 2.9, Bash.

## Global Constraints

- Preserve unrelated dirty files and stage only files named by the active task.
- Use only `http://mkpservice.shippop.dev` with `SHIPPOP_ALLOW_INSECURE_HTTP=1` for this isolated Development exercise.
- Use synthetic provider-approved contacts and addresses; never use real customer data.
- API keys and account email remain environment secrets and never enter source, test output, evidence JSON, logs, or Git.
- Do not persist raw SHIPPOP requests, responses, QR values, labels, tracking numbers, purchase references, phones, or addresses.
- Do not derive a QR from tracking, purchase, label barcode, or another inferred value.
- Do not parse or extract a QR from 4×6 label HTML.
- Do not add a speculative SHIPPOP endpoint or production response field.
- An observed candidate is not certified counter-QR support and cannot enable a service.
- `EMST`, `FLE`, `KRYX`, and `KRYS` remain independently disabled until their complete reviewed capability matrix passes.
- Never replay an outcome-unknown mutation merely to obtain an observation.
- Cleanup failure blocks the result and is reported without provider text.
- No application state, snapshot, carrier event, dispute, refund, or payout behavior changes in this phase.

---

## Scope boundary and follow-on gate

This slice answers one question:

```text
Does the exact SHIPPOP Dev booking or confirm response for this account/service
contain a field that could be the official counter-QR artifact?
```

It does not implement the mobile card, database resource, Worker task, seller
API, capability enablement, or label UI. A follow-on plan requires:

- the exact official endpoint and field;
- image or provider-counter-payload representation;
- counter-use purpose for the specific service;
- expiry or rotation semantics;
- a read-only post-confirmation retrieval/refresh path;
- repeated-read behavior that cannot repeat booking or confirmation; and
- controlled carrier/counter acceptance evidence.

If no candidate appears, record `not_observed` and obtain SHIPPOP's private read
contract. Do not guess one.

## File map

- Create `tests/Toklong.Shippop.Certification/CounterQrResponseShape.cs` — value-free JSON shape parser.
- Create `tests/Toklong.Shippop.Certification/CounterQrResponseShapeTests.cs` — sanitizer tests.
- Create `tests/Toklong.Shippop.Certification/CounterQrObservationHandler.cs` — certification-only HTTP observer.
- Create `tests/Toklong.Shippop.Certification/CounterQrObservationHandlerTests.cs` — response-preservation tests.
- Create `tests/Toklong.Shippop.Certification/CounterQrCertificationContext.cs` — guarded environment and synthetic fixture loader.
- Create `tests/Toklong.Shippop.Certification/CounterQrEvidenceReport.cs` — outside-repository sanitized report.
- Create `tests/Toklong.Shippop.Certification/CounterQrCertificationTests.cs` — offline guards and live lifecycle.
- Modify `scripts/shippop-certify.sh` — explicit `counter-qr-observe` mode.
- Modify `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md` — operator instructions and outcome meanings.
- Modify `docs/06_OPEN_DECISIONS.md` — retain the official read-path blocker.

The production provider, Application abstractions, Domain, persistence, API,
Worker, MAUI, and production settings remain unchanged.

---

### Task 1: Build the value-free response-shape sanitizer

**Files:**
- Create: `tests/Toklong.Shippop.Certification/CounterQrResponseShape.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrResponseShapeTests.cs`

**Interfaces:**
- Produces: `CounterQrResponseShapeParser.Parse(string endpoint, ReadOnlySpan<byte> utf8Json)`.
- Produces: `CounterQrResponseShape(string Endpoint, IReadOnlyList<CounterQrFieldObservation> Fields)` and derived `CandidatePaths`.
- Produces: `CounterQrFieldObservation(string Path, JsonValueKind Kind, string? StringLengthBucket, bool IsCandidate)`.

- [ ] **Step 1: Write the failing redaction tests**

```csharp
using System.Text;
using System.Text.Json;

namespace Toklong.Shippop.Certification;

public sealed class CounterQrResponseShapeTests
{
    [Fact]
    public void Parser_records_paths_without_provider_values()
    {
        var json = Encoding.UTF8.GetBytes(
            """
            {"purchase_id":"452002","result":{"0":{
              "counter_qr":"SECRET-COUNTER-VALUE",
              "receiver_address":"99 Customer Road",
              "courier_tracking_code":"EF123456789TH"}}}
            """);

        var shape = CounterQrResponseShapeParser.Parse("confirm/", json);
        var serialized = JsonSerializer.Serialize(shape);

        Assert.Contains("$.result[].counter_qr", shape.CandidatePaths);
        Assert.Contains(shape.Fields, field =>
            field.Path == "$.result[].receiver_address" &&
            field.Kind == JsonValueKind.String);
        Assert.DoesNotContain("SECRET-COUNTER-VALUE", serialized);
        Assert.DoesNotContain("99 Customer Road", serialized);
        Assert.DoesNotContain("EF123456789TH", serialized);
        Assert.DoesNotContain("452002", serialized);
    }

    [Fact]
    public void Parser_masks_dynamic_provider_keys()
    {
        var json = Encoding.UTF8.GetBytes(
            """{"result":{"SP-PRIVATE-123":{"value":"secret"}}}""");

        var serialized = JsonSerializer.Serialize(
            CounterQrResponseShapeParser.Parse("booking/", json));

        Assert.Contains("$.result.*.value", serialized);
        Assert.DoesNotContain("SP-PRIVATE-123", serialized);
        Assert.DoesNotContain("secret", serialized);
    }

    [Fact]
    public void Parser_rejects_more_than_five_megabytes()
    {
        var bytes = new byte[(5 * 1024 * 1024) + 1];

        Assert.Throws<InvalidOperationException>(() =>
            CounterQrResponseShapeParser.Parse("confirm/", bytes));
    }
}
```

- [ ] **Step 2: Run the tests and confirm the missing-type failure**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~CounterQrResponseShapeTests
```

Expected: FAIL because the parser and records do not exist.

- [ ] **Step 3: Implement the bounded parser**

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Toklong.Shippop.Certification;

internal sealed record CounterQrFieldObservation(
    string Path,
    JsonValueKind Kind,
    string? StringLengthBucket,
    bool IsCandidate);

internal sealed record CounterQrResponseShape(
    string Endpoint,
    IReadOnlyList<CounterQrFieldObservation> Fields)
{
    public IReadOnlyList<string> CandidatePaths => Fields
        .Where(field => field.IsCandidate)
        .Select(field => field.Path)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

internal static partial class CounterQrResponseShapeParser
{
    internal const int MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumDepth = 12;
    private const int MaximumFields = 256;

    [GeneratedRegex("^[a-z][a-z_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFieldName();

    internal static CounterQrResponseShape Parse(
        string endpoint,
        ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length is 0 or > MaximumBytes)
            throw new InvalidOperationException(
                "Counter QR observation response size is invalid.");
        using var document = JsonDocument.Parse(
            utf8Json,
            new JsonDocumentOptions { MaxDepth = MaximumDepth });
        var fields = new List<CounterQrFieldObservation>();
        Visit(document.RootElement, "$", fields, 0);
        return new CounterQrResponseShape(
            NormalizeEndpoint(endpoint), fields);
    }

    private static void Visit(
        JsonElement element,
        string path,
        List<CounterQrFieldObservation> fields,
        int depth)
    {
        if (depth > MaximumDepth || fields.Count >= MaximumFields)
            throw new InvalidOperationException(
                "Counter QR observation response shape is too complex.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = SafeFieldName().IsMatch(property.Name)
                    ? property.Name
                    : property.Name.All(char.IsAsciiDigit) ? "[]" : "*";
                var childPath = name == "[]"
                    ? $"{path}[]" : $"{path}.{name}";
                fields.Add(new CounterQrFieldObservation(
                    childPath,
                    property.Value.ValueKind,
                    property.Value.ValueKind == JsonValueKind.String
                        ? Bucket(property.Value.GetString()?.Length ?? 0)
                        : null,
                    name is not ("*" or "[]") && IsCandidate(name)));
                Visit(property.Value, childPath, fields, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                Visit(child, $"{path}[]", fields, depth + 1);
        }
    }

    private static bool IsCandidate(string name) =>
        name.Contains("qr", StringComparison.Ordinal) ||
        name.Contains("barcode", StringComparison.Ordinal) ||
        name.Contains("counter", StringComparison.Ordinal) ||
        name.Contains("dropoff", StringComparison.Ordinal) ||
        name.Contains("drop_off", StringComparison.Ordinal);

    private static string Bucket(int length) => length switch
    {
        0 => "empty", <= 32 => "1-32", <= 128 => "33-128",
        <= 1_024 => "129-1024", _ => "over-1024"
    };

    private static string NormalizeEndpoint(string endpoint) =>
        endpoint.Trim().TrimStart('/') switch
        {
            "booking" or "booking/" => "booking/",
            "confirm" or "confirm/" => "confirm/",
            _ => throw new InvalidOperationException(
                "Counter QR observation endpoint is not allow-listed.")
        };
}
```

Never call `JsonElement.ToString()`, store a provider-value digest, or retain
the input byte array on an object.

- [ ] **Step 4: Run the Task 1 tests**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit the sanitizer**

```bash
git add tests/Toklong.Shippop.Certification/CounterQrResponseShape.cs tests/Toklong.Shippop.Certification/CounterQrResponseShapeTests.cs
git commit -m "test: sanitize SHIPPOP counter QR response shapes"
```

---

### Task 2: Capture only booking and confirmation shapes

**Files:**
- Create: `tests/Toklong.Shippop.Certification/CounterQrObservationHandler.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrObservationHandlerTests.cs`

**Interfaces:**
- Consumes: `CounterQrResponseShapeParser.Parse(...)`.
- Produces: `CounterQrObservationHandler(HttpMessageHandler innerHandler)` with thread-safe `Observations` and sanitized `FailureCodes`.
- Preserves: status, content bytes, and content headers for the existing provider parser.

- [ ] **Step 1: Write the failing handler tests**

```csharp
using System.Net;
using System.Text;

namespace Toklong.Shippop.Certification;

public sealed class CounterQrObservationHandlerTests
{
    [Fact]
    public async Task Handler_observes_confirm_and_preserves_content()
    {
        const string body =
            "{\"status\":true,\"counter_qr\":\"SECRET\"}";
        var observer = new CounterQrObservationHandler(
            new StubHandler(body));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var response = await client.PostAsync(
            "confirm/", new StringContent("request"));

        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal("confirm/", Assert.Single(observer.Observations).Endpoint);
    }

    [Fact]
    public async Task Handler_ignores_pricelist_and_tracking()
    {
        var observer = new CounterQrObservationHandler(
            new StubHandler("{\"counter_qr\":\"SECRET\"}"));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        await client.PostAsync("pricelist/", new StringContent("request"));
        await client.PostAsync("tracking/", new StringContent("request"));

        Assert.Empty(observer.Observations);
    }

    [Fact]
    public async Task Handler_preserves_malformed_content_and_records_only_a_safe_failure()
    {
        const string body = "not-json SECRET";
        var observer = new CounterQrObservationHandler(
            new StubHandler(body));
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("http://mkpservice.shippop.dev/")
        };

        using var response = await client.PostAsync(
            "confirm/", new StringContent("request"));

        Assert.Equal(body, await response.Content.ReadAsStringAsync());
        Assert.Equal(["unsafe_response_shape"], observer.FailureCodes);
        Assert.Empty(observer.Observations);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    body, Encoding.UTF8, "application/json")
            });
    }
}
```

- [ ] **Step 2: Run the tests and confirm the missing-handler failure**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~CounterQrObservationHandlerTests
```

Expected: FAIL because the handler does not exist.

- [ ] **Step 3: Implement the certification-only handler**

```csharp
using System.Text.Json;

namespace Toklong.Shippop.Certification;

internal sealed class CounterQrObservationHandler(
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private readonly object sync = new();
    private readonly List<CounterQrResponseShape> observations = [];
    private readonly List<string> failureCodes = [];

    internal IReadOnlyList<CounterQrResponseShape> Observations
    {
        get { lock (sync) return observations.ToArray(); }
    }

    internal IReadOnlyList<string> FailureCodes
    {
        get { lock (sync) return failureCodes.ToArray(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var endpoint = Endpoint(request.RequestUri);
        if (endpoint is null || response.Content is null)
            return response;

        var original = response.Content;
        var bytes = await original.ReadAsByteArrayAsync(cancellationToken);
        var replacement = new ByteArrayContent(bytes);
        foreach (var header in original.Headers)
            replacement.Headers.TryAddWithoutValidation(
                header.Key, header.Value);
        response.Content = replacement;
        original.Dispose();

        try
        {
            var shape = CounterQrResponseShapeParser.Parse(endpoint, bytes);
            lock (sync) observations.Add(shape);
        }
        catch (JsonException)
        {
            lock (sync) failureCodes.Add("unsafe_response_shape");
        }
        catch (InvalidOperationException)
        {
            lock (sync) failureCodes.Add("unsafe_response_shape");
        }
        return response;
    }

    private static string? Endpoint(Uri? uri) =>
        uri?.AbsolutePath.Trim('/').ToLowerInvariant() switch
        {
            "booking" => "booking/",
            "confirm" => "confirm/",
            _ => null
        };
}
```

The handler has no event exposing bytes and performs no console, disk, or log
write. Shape parsing can never prevent the unchanged provider adapter from
receiving its response; only the allow-listed failure code is retained.

- [ ] **Step 4: Run Task 1 and Task 2 tests together**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter "FullyQualifiedName~CounterQrResponseShapeTests|FullyQualifiedName~CounterQrObservationHandlerTests"
```

Expected: PASS with no provider value in output.

- [ ] **Step 5: Commit the handler**

```bash
git add tests/Toklong.Shippop.Certification/CounterQrObservationHandler.cs tests/Toklong.Shippop.Certification/CounterQrObservationHandlerTests.cs
git commit -m "test: observe SHIPPOP booking and confirm shapes"
```

---

### Task 3: Add the isolated real-Sandbox lifecycle

**Files:**
- Create: `tests/Toklong.Shippop.Certification/CounterQrCertificationContext.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrEvidenceReport.cs`
- Create: `tests/Toklong.Shippop.Certification/CounterQrCertificationTests.cs`

**Interfaces:**
- Consumes: `CounterQrObservationHandler` and existing provider quote/reserve/confirm/cancel methods.
- Produces: `CounterQrCertificationContext.LoadAsync()` and `CreateProvider(CounterQrObservationHandler)`.
- Produces: `CounterQrEvidenceReport.Write(...)` with no raw values.
- Produces: live `Observe_booking_and_confirm_for_counter_qr_candidate`, guarded by `[CertificationFact]` and `SHIPPOP_CERTIFY_MUTATIONS=1`.

- [ ] **Step 1: Write failing offline guard tests**

```csharp
namespace Toklong.Shippop.Certification;

public sealed class CounterQrCertificationTests
{
    [Theory]
    [InlineData("https://mkpservice.shippop.com", true)]
    [InlineData("http://mkpservice.shippop.dev/", true)]
    [InlineData("http://mkpservice.shippop.dev", false)]
    public void Context_rejects_unapproved_origin_or_missing_opt_in(
        string baseUrl,
        bool allowInsecureHttp)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CounterQrCertificationContext.EnsureApprovedEndpoint(
                baseUrl, allowInsecureHttp));
    }

    [Fact]
    public void Evidence_writer_rejects_a_repository_descendant()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CounterQrEvidenceReport.EnsureOutsideRepository(
                "/work/Toklong",
                "/work/Toklong/TestResults/qr"));
    }

    [Fact]
    public void Evidence_document_cannot_hold_artifact_references()
    {
        var names = typeof(CounterQrEvidenceDocument)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(names, name =>
            name.Contains("Value", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Artifact", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Tracking", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Purchase", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Address", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run offline tests and verify missing types**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj --filter FullyQualifiedName~CounterQrCertificationTests
```

Expected: FAIL because the context and report do not exist.

- [ ] **Step 3: Implement guarded environment loading**

Create this contract in `CounterQrCertificationContext.cs`:

```csharp
internal sealed record CounterQrCertificationContext(
    string ServiceCode,
    ShippingQuoteRequest Shipment,
    string EvidenceDirectory,
    string BaseUrl,
    bool AllowInsecureHttp,
    string ApiKey,
    string AccountEmail)
{
    internal static async Task<CounterQrCertificationContext> LoadAsync();

    internal ShippopShippingProvider CreateProvider(
        CounterQrObservationHandler observer);

    internal static void EnsureApprovedEndpoint(
        string baseUrl,
        bool allowInsecureHttp);
}
```

Implement `LoadAsync()` in this safety order:

1. Require `SHIPPOP_BASE_URL` and `SHIPPOP_ALLOW_INSECURE_HTTP=1`; accept only
   the exact string `http://mkpservice.shippop.dev`.
2. Require `SHIPPOP_REPOSITORY_ROOT` and `SHIPPOP_EVIDENCE_DIRECTORY`, resolve
   both with `Path.GetFullPath`, and reject equality or a repository descendant.
3. Require `SHIPPOP_SERVICE_CODE`; allow only
   `ShippopShippingOptions.SupportedServiceCodes`.
4. Require exact `SHIPPOP_CERTIFY_MUTATIONS=1`; otherwise throw only
   `counter-qr-mutation-observation-disabled`.
5. Read `SHIPPOP_SYNTHETIC_ADDRESS_JSON` into the documented
   `ShippingQuoteRequest` fields.
6. After all guards pass, read `SHIPPOP_API_KEY` and
   `SHIPPOP_ACCOUNT_EMAIL`.

`CreateProvider(...)` uses `HttpClient(observer)` with the approved base and a
30-second timeout. Configure only the selected service with:

```csharp
new ShippopServiceProfile(
    ServiceCode,
    QuoteEnabled: true,
    BookOutboundEnabled: true,
    ConfirmEnabled: true,
    ReturnEnabled: false,
    InsuranceEnabled: false,
    OperationLookupEnabled: true,
    HandoffMode: "DropOff",
    MaximumCoverageSatang: 0,
    CertificationReference: "COUNTER-QR-OBSERVATION-ONLY",
    IncludedCoverageSatang: 0,
    OptionalProtectionEnabled: false)
```

Use `ServiceCodes = [ServiceCode]` and the certification-only local signing
value `counter-qr-observation-signing-key-32`. It is not a provider credential.

- [ ] **Step 4: Implement the allow-listed report**

Create `CounterQrEvidenceReport.cs`:

```csharp
internal sealed record CounterQrEvidenceDocument(
    string ServiceCode,
    DateTimeOffset RecordedAtUtc,
    string Result,
    string CleanupOutcome,
    IReadOnlyList<string> ObservationFailureCodes,
    IReadOnlyList<CounterQrResponseShape> Responses);

internal static class CounterQrEvidenceReport
{
    private static readonly IReadOnlySet<string> Results =
        new HashSet<string>(
            ["candidate_observed", "not_observed",
             "cleanup_failed", "execution_blocked"],
            StringComparer.Ordinal);

    internal static string Write(
        string directory,
        CounterQrEvidenceDocument document);

    internal static void EnsureOutsideRepository(
        string repositoryRoot,
        string evidenceDirectory);
}
```

`EnsureOutsideRepository` compares normalized full paths with a trailing
directory separator. `Write` accepts only the four results and only the failure
code `unsafe_response_shape`, creates the directory with Unix mode `700`,
writes the JSON file with mode `600`, and returns its path without printing the
document. The filename contains only the lowercase service code, literal
`counter-qr`, and a UTC timestamp.

- [ ] **Step 5: Add the live lifecycle without a new endpoint**

Add this test to `CounterQrCertificationTests.cs`:

```csharp
[CertificationFact]
public async Task Observe_booking_and_confirm_for_counter_qr_candidate()
{
    var context = await CounterQrCertificationContext.LoadAsync();
    var observer = new CounterQrObservationHandler(new HttpClientHandler());
    var provider = context.CreateProvider(observer);
    var result = "execution_blocked";
    var cleanup = "cleanup_unavailable";
    string? trackingForCleanup = null;

    try
    {
        var quote = Assert.Single(
            await provider.GetQuotesAsync(context.Shipment, default),
            candidate => candidate.ServiceCode == context.ServiceCode);
        var shipmentId = Guid.NewGuid();
        var reservation = await provider.ReserveAsync(
            new ShipmentReservationRequest(
                Guid.NewGuid(), context.Shipment, quote, shipmentId,
                IsReturn: false,
                OperationReference: $"cert-qr-{shipmentId:N}"),
            default);
        trackingForCleanup = reservation.CourierTrackingCode;
        var confirmation = await provider.ConfirmServiceAsync(
            reservation.PurchaseReference,
            reservation.ProviderTrackingCode,
            reservation.CarrierCode,
            reservation.ServiceCode,
            default);
        trackingForCleanup = confirmation.CourierTrackingCode;

        var candidates = observer.Observations
            .SelectMany(response => response.CandidatePaths)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        result = observer.FailureCodes.Count > 0
            ? "execution_blocked"
            : candidates.Length > 0
                ? "candidate_observed"
                : "not_observed";
    }
    catch
    {
        result = "execution_blocked";
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(trackingForCleanup))
        {
            try
            {
                await provider.CancelServiceAsync(
                    trackingForCleanup,
                    context.ServiceCode,
                    IsReturn: false,
                    default);
                cleanup = "cancelled";
            }
            catch
            {
                cleanup = "cleanup_failed";
            }
        }
        if (cleanup != "cancelled")
            result = "cleanup_failed";

        CounterQrEvidenceReport.Write(
            context.EvidenceDirectory,
            new CounterQrEvidenceDocument(
                context.ServiceCode,
                DateTimeOffset.UtcNow,
                result,
                cleanup,
                observer.FailureCodes,
                observer.Observations));
    }

    Assert.Equal("cancelled", cleanup);
    Assert.Equal("candidate_observed", result);
}
```

Do not call `GetLabelHtmlAsync`, retry reservation/confirmation, or add another
HTTP route. `not_observed`, `execution_blocked`, and `cleanup_failed` fail the
sanitized assertion after the report is safely written. If reservation succeeds
without a cancellable tracking reference and confirmation fails, cleanup is
`cleanup_unavailable`, the result becomes `cleanup_failed`, and the operator
must stop that service rather than retrying.

- [ ] **Step 6: Run all offline certification tests**

```bash
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
```

Expected: ordinary tests PASS; live `[CertificationFact]` tests SKIP without
`SHIPPOP_CERTIFY=1`.

- [ ] **Step 7: Commit the lifecycle**

```bash
git add tests/Toklong.Shippop.Certification/CounterQrCertificationContext.cs tests/Toklong.Shippop.Certification/CounterQrEvidenceReport.cs tests/Toklong.Shippop.Certification/CounterQrCertificationTests.cs
git commit -m "test: add SHIPPOP counter QR observation lifecycle"
```

---

### Task 4: Add the command and provider-checkpoint runbook

**Files:**
- Modify: `scripts/shippop-certify.sh`
- Modify: `docs/SHIPPOP_CERTIFICATION_RUNBOOK.md`
- Modify: `docs/06_OPEN_DECISIONS.md`

**Interfaces:**
- Produces: `./scripts/shippop-certify.sh counter-qr-observe`.
- Preserves: current no-argument parcel-protection behavior.
- Consumes: environment contract enforced by `CounterQrCertificationContext`.

- [ ] **Step 1: Establish the shell baseline**

```bash
bash -n scripts/shippop-certify.sh
```

Expected: PASS.

- [ ] **Step 2: Add the explicit mode**

Add this mode selection near the top of `scripts/shippop-certify.sh`:

```bash
mode="${1:-parcel-protection}"
case "${mode}" in
  parcel-protection)
    test_filter="FullyQualifiedName~Protection_quote_and_booking_preserve_exact_values"
    ;;
  counter-qr-observe)
    test_filter="FullyQualifiedName~Observe_booking_and_confirm_for_counter_qr_candidate"
    ;;
  *)
    echo "Usage: ./scripts/shippop-certify.sh [parcel-protection|counter-qr-observe]" >&2
    exit 2
    ;;
esac
```

For `counter-qr-observe`, require `SHIPPOP_EVIDENCE_DIRECTORY` and exact
`SHIPPOP_CERTIFY_MUTATIONS=1`. Set `umask 077` and export:

```bash
export SHIPPOP_CERTIFY=1
export SHIPPOP_REPOSITORY_ROOT="$(pwd -P)"
```

Use `--filter "${test_filter}"`. Do not echo environment values.

- [ ] **Step 3: Document the exact operator command**

Append `## Counter QR contract observation` to
`docs/SHIPPOP_CERTIFICATION_RUNBOOK.md` with:

```bash
mkdir -p /private/tmp/shippop-counter-qr-evidence
chmod 700 /private/tmp/shippop-counter-qr-evidence

SHIPPOP_BASE_URL=http://mkpservice.shippop.dev \
SHIPPOP_ALLOW_INSECURE_HTTP=1 \
SHIPPOP_API_KEY="$SHIPPOP_API_KEY" \
SHIPPOP_ACCOUNT_EMAIL="$SHIPPOP_ACCOUNT_EMAIL" \
SHIPPOP_SERVICE_CODE=EMST \
SHIPPOP_SYNTHETIC_ADDRESS_JSON="$SHIPPOP_SYNTHETIC_ADDRESS_JSON" \
SHIPPOP_EVIDENCE_DIRECTORY=/private/tmp/shippop-counter-qr-evidence \
SHIPPOP_CERTIFY_MUTATIONS=1 \
./scripts/shippop-certify.sh counter-qr-observe
```

Document these outcomes:

- `candidate_observed`: a safe field path matched the name rules; discovery
  only, never service enablement.
- `not_observed`: existing booking/confirm responses expose no candidate;
  request an authenticated read endpoint/field from SHIPPOP.
- `cleanup_failed`: stop that service and resolve the synthetic shipment before
  another mutation.
- `execution_blocked`: configuration, provider response, or mutation outcome
  prevented a safe observation.

State that the report contains no QR and cannot be scanned. A controlled scan
comes only after the official retrievable artifact is established.

- [ ] **Step 4: Record the remaining launch blocker**

Add under Shipping in `docs/06_OPEN_DECISIONS.md`:

```markdown
- Counter-QR response observation is discovery only. A candidate field in a
  booking or confirmation response does not enable a service. Production
  remains blocked until SHIPPOP documents the official counter purpose, exact
  representation, expiry/rotation behavior, a read-only post-confirmation
  retrieval path with safe repeated-read semantics, and controlled counter-
  acceptance evidence for the specific account and service code.
```

- [ ] **Step 5: Run verification**

```bash
bash -n scripts/shippop-certify.sh
dotnet test tests/Toklong.Shippop.Certification/Toklong.Shippop.Certification.csproj
dotnet test Toklong.slnx --no-restore
git diff --check
git status --short
```

Expected: shell and ordinary tests pass; live facts skip without opt-in; only
documented environment-dependent skips remain; no secret, fixture, response,
QR, label, tracking, purchase reference, or evidence JSON appears in Git.

- [ ] **Step 6: Commit the command and runbook**

```bash
git add scripts/shippop-certify.sh docs/SHIPPOP_CERTIFICATION_RUNBOOK.md docs/06_OPEN_DECISIONS.md
git commit -m "docs: add SHIPPOP counter QR observation runbook"
```

---

## Provider checkpoint: run one service at a time

This checkpoint creates one real synthetic Dev booking, confirmation, and
cancellation. Never run it unattended or in parallel.

1. Rotate any credential previously pasted into chat or committed.
2. Confirm the synthetic fixture is approved for SHIPPOP Dev.
3. Create the outside-repository evidence directory with mode `700`.
4. Run the runbook command for one service code.
5. Inspect only the sanitized result and safe field paths.
6. If cleanup is not `cancelled`, stop and do not rerun that service.
7. For `not_observed`, request the official read contract and keep the service
   disabled.
8. For `candidate_observed`, obtain written purpose and safe read/retry evidence;
   do not enable from the candidate alone.

No repository commit occurs at this checkpoint because evidence stays outside
source control.

## Definition of done

- Provider values cannot enter the observation model.
- Only `booking/` and `confirm/` are captured, without changing adapter parsing.
- Live observation is explicit, mutation-gated, sequential, and cleanup-aware.
- Sanitized evidence is outside the repository with user-only permissions.
- Candidate observation is clearly distinct from certification.
- Production adapter, capability flags, API, Worker, database, MAUI, and states
  remain unchanged.
- The follow-on plan is written only after an official read/retry contract is
  available; it will cover service gating, encrypted shipment resource, Worker
  retrieval, seller-only `no-store` API, download-only label UI, three mobile
  states, accessibility, and source-of-truth documentation updates.
