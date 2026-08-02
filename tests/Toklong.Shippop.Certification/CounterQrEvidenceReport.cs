using System.Text.Json;
using Toklong.Infrastructure.Services;

namespace Toklong.Shippop.Certification;

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
            [
                "candidate_observed",
                "not_observed",
                "cleanup_failed",
                "execution_blocked"
            ],
            StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> CleanupOutcomes =
        new HashSet<string>(
            [
                "cancelled",
                "cleanup_failed",
                "cleanup_unavailable"
            ],
            StringComparer.Ordinal);

    internal static string Write(
        string directory,
        CounterQrEvidenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Results.Contains(document.Result) ||
            !CleanupOutcomes.Contains(document.CleanupOutcome) ||
            document.ObservationFailureCodes.Any(code =>
                !string.Equals(
                    code,
                    "unsafe_response_shape",
                    StringComparison.Ordinal)) ||
            !ShippopShippingOptions.SupportedServiceCodes.Contains(
                document.ServiceCode))
            throw new InvalidOperationException(
                "Counter QR evidence document is invalid.");

        var fullDirectory = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Counter QR evidence requires Unix file permissions.");
        Directory.CreateDirectory(fullDirectory);
        File.SetUnixFileMode(
            fullDirectory,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);

        var timestamp = document.RecordedAtUtc
            .ToUniversalTime()
            .ToString(
                "yyyyMMdd'T'HHmmssfff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);
        var fileName =
            $"{document.ServiceCode.ToLowerInvariant()}-counter-qr-{timestamp}.json";
        var path = Path.Combine(fullDirectory, fileName);
        var json = JsonSerializer.SerializeToUtf8Bytes(
            document,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
        using (var stream = new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
            stream.Write(json);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);
        return path;
    }

    internal static void EnsureOutsideRepository(
        string repositoryRoot,
        string evidenceDirectory)
    {
        var repository = WithSeparator(
            Path.GetFullPath(repositoryRoot));
        var evidence = WithSeparator(
            Path.GetFullPath(evidenceDirectory));
        if (string.Equals(
                repository,
                evidence,
                StringComparison.Ordinal) ||
            evidence.StartsWith(
                repository,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Counter QR evidence directory must be outside the repository.");
    }

    private static string WithSeparator(string path) =>
        path.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
}
