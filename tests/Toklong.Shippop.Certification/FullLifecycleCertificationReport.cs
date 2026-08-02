using System.Text.Json;

namespace Toklong.Shippop.Certification;

internal static class FullLifecycleCertificationReport
{
    private static readonly string[] Capabilities =
    [
        "pricelist",
        "booking",
        "confirm",
        "label",
        "tracking",
        "cancel"
    ];

    private static readonly IReadOnlySet<string> AllowedReasons =
        new HashSet<string>(
            [
                "not_reached",
                "mutation_disabled",
                "quote_missing",
                "quote_price_invalid",
                "quote_failed",
                "quote_valid",
                "booking_failed",
                "booking_contract_invalid",
                "booking_valid",
                "confirm_failed",
                "confirm_contract_invalid",
                "confirm_valid",
                "label_failed",
                "label_contract_invalid",
                "label_valid",
                "tracking_failed",
                "tracking_contract_invalid",
                "tracking_valid",
                "cancel_confirmed",
                "cleanup_required"
            ],
            StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    public static string Serialize(
        FullLifecycleCertificationResult result,
        DateTimeOffset recordedAtUtc)
    {
        EnsureAllowListed(result);
        var rows = result.Checks
            .Select(check => new SanitizedLifecycleRow(
                check.Capability,
                Outcome(check.Outcome),
                check.ReasonCode))
            .ToArray();
        return JsonSerializer.Serialize(
            new SanitizedLifecycleDocument(
                "shippop-sandbox",
                recordedAtUtc,
                rows,
                result.Passed),
            JsonOptions);
    }

    public static void Write(
        FullLifecycleCertificationResult result,
        DateTimeOffset recordedAtUtc,
        TextWriter output) =>
        output.WriteLine(Serialize(result, recordedAtUtc));

    private static void EnsureAllowListed(
        FullLifecycleCertificationResult result)
    {
        if (result.Checks.Count != Capabilities.Length ||
            result.Checks
                .Select(check => check.Capability)
                .Distinct(StringComparer.Ordinal)
                .Count() != Capabilities.Length)
            throw new InvalidOperationException(
                "Certification report shape is not allow-listed.");

        for (var index = 0; index < Capabilities.Length; index++)
        {
            var check = result.Checks[index];
            if (!string.Equals(
                    check.Capability,
                    Capabilities[index],
                    StringComparison.Ordinal) ||
                !AllowedReasons.Contains(check.ReasonCode) ||
                !OutcomeMatchesReason(check))
                throw new InvalidOperationException(
                    "Certification report entry is not allow-listed.");
        }
    }

    private static bool OutcomeMatchesReason(
        FullLifecycleCheck check) =>
        check.Outcome switch
        {
            FullLifecycleOutcome.Pass =>
                check.ReasonCode is
                    "quote_valid" or
                    "booking_valid" or
                    "confirm_valid" or
                    "label_valid" or
                    "tracking_valid" or
                    "cancel_confirmed",
            FullLifecycleOutcome.Fail =>
                check.ReasonCode.EndsWith(
                    "_failed",
                    StringComparison.Ordinal) ||
                check.ReasonCode.EndsWith(
                    "_invalid",
                    StringComparison.Ordinal) ||
                check.ReasonCode == "quote_missing",
            FullLifecycleOutcome.Blocked =>
                check.ReasonCode is
                    "not_reached" or
                    "mutation_disabled",
            FullLifecycleOutcome.CleanupRequired =>
                check.ReasonCode == "cleanup_required",
            _ => false
        };

    private static string Outcome(
        FullLifecycleOutcome outcome) =>
        outcome switch
        {
            FullLifecycleOutcome.Pass => "pass",
            FullLifecycleOutcome.Fail => "fail",
            FullLifecycleOutcome.Blocked => "blocked",
            FullLifecycleOutcome.CleanupRequired =>
                "cleanup_required",
            _ => throw new InvalidOperationException(
                "Certification outcome is not allow-listed.")
        };

    private sealed record SanitizedLifecycleDocument(
        string Environment,
        DateTimeOffset RecordedAtUtc,
        IReadOnlyList<SanitizedLifecycleRow> Checks,
        bool Passed);

    private sealed record SanitizedLifecycleRow(
        string Capability,
        string Outcome,
        string ReasonCode);
}
