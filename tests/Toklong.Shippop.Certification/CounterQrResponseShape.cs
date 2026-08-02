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
        .Where(observation => observation.IsCandidate)
        .Select(observation => observation.Path)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
}

internal static partial class CounterQrResponseShapeParser
{
    internal const int MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumDepth = 12;
    private const int MaximumFields = 256;
    private const int MaximumVisitedNodes = 1_024;

    [GeneratedRegex("^[a-z][a-z_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFieldName();

    internal static CounterQrResponseShape Parse(
        string endpoint,
        ReadOnlySpan<byte> utf8Json)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        if (utf8Json.Length is 0 or > MaximumBytes)
            throw new InvalidOperationException(
                "Counter QR observation response size is invalid.");

        using var document = JsonDocument.Parse(
            utf8Json.ToArray(),
            new JsonDocumentOptions { MaxDepth = MaximumDepth });
        var fields = new List<CounterQrFieldObservation>();
        var visitedNodes = 0;
        Visit(
            document.RootElement,
            "$",
            fields,
            0,
            ref visitedNodes);
        return new CounterQrResponseShape(
            normalizedEndpoint,
            fields);
    }

    private static void Visit(
        JsonElement element,
        string path,
        List<CounterQrFieldObservation> fields,
        int depth,
        ref int visitedNodes)
    {
        visitedNodes++;
        if (depth > MaximumDepth ||
            fields.Count >= MaximumFields ||
            visitedNodes > MaximumVisitedNodes)
            throw new InvalidOperationException(
                "Counter QR observation response shape is too complex.");

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = SafeFieldName().IsMatch(property.Name)
                    ? property.Name
                    : property.Name.Length > 0 &&
                      property.Name.All(char.IsAsciiDigit)
                        ? "[]"
                        : "*";
                var childPath = name == "[]"
                    ? $"{path}[]"
                    : $"{path}.{name}";
                fields.Add(new CounterQrFieldObservation(
                    childPath,
                    property.Value.ValueKind,
                    property.Value.ValueKind == JsonValueKind.String
                        ? Bucket(property.Value.GetString()?.Length ?? 0)
                        : null,
                    name is not ("*" or "[]") && IsCandidate(name)));
                Visit(
                    property.Value,
                    childPath,
                    fields,
                    depth + 1,
                    ref visitedNodes);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                Visit(
                    child,
                    $"{path}[]",
                    fields,
                    depth + 1,
                    ref visitedNodes);
        }
    }

    private static bool IsCandidate(string name) =>
        name.Contains("qr", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("barcode", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("counter", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("dropoff", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("drop_off", StringComparison.OrdinalIgnoreCase);

    private static string Bucket(int length) => length switch
    {
        0 => "empty",
        <= 32 => "1-32",
        <= 128 => "33-128",
        <= 1_024 => "129-1024",
        _ => "over-1024"
    };

    private static string NormalizeEndpoint(string endpoint) =>
        endpoint?.Trim().TrimStart('/') switch
        {
            "booking" or "booking/" => "booking/",
            "confirm" or "confirm/" => "confirm/",
            _ => throw new InvalidOperationException(
                "Counter QR observation endpoint is not allow-listed.")
        };
}
