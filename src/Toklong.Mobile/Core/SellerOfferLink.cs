namespace Toklong.Mobile.Core;

public static class SellerOfferLink
{
    public static bool TryGetPublicToken(
        string? value,
        out string publicToken)
    {
        publicToken = "";
        return !string.IsNullOrWhiteSpace(value) &&
               Uri.TryCreate(
                   value.Trim(),
                   UriKind.Absolute,
                   out var uri) &&
               TryGetPublicToken(uri, out publicToken);
    }

    public static bool TryGetPublicToken(
        Uri? uri,
        out string publicToken)
    {
        publicToken = "";
        if (uri is null)
            return false;

        string? candidate = null;
        if (uri.Scheme.Equals("toklong", StringComparison.OrdinalIgnoreCase) &&
            uri.Host.Equals("offer", StringComparison.OrdinalIgnoreCase))
        {
            candidate = uri.AbsolutePath.Trim('/');
        }
        else if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                 IsOwnedHost(uri.Host))
        {
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2 &&
                segments[0].Equals(
                    "offer",
                    StringComparison.OrdinalIgnoreCase))
                candidate = segments[1];
        }

        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length is < 32 or > 64 ||
            candidate.Any(character => !Uri.IsHexDigit(character)))
            return false;

        publicToken = candidate.ToLowerInvariant();
        return true;
    }

    private static bool IsOwnedHost(string host) =>
        host.Equals(
            "toklong.co.th",
            StringComparison.OrdinalIgnoreCase) ||
        host.Equals(
            "www.toklong.co.th",
            StringComparison.OrdinalIgnoreCase) ||
        host.Equals(
            "app.toklong.co.th",
            StringComparison.OrdinalIgnoreCase);
}

public static class TransactionLink
{
    public static bool TryGetTransactionId(
        Uri? uri,
        out Guid transactionId)
    {
        transactionId = Guid.Empty;
        if (uri is null ||
            !uri.Scheme.Equals(
                "toklong",
                StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals(
                "transaction",
                StringComparison.OrdinalIgnoreCase))
            return false;

        return Guid.TryParse(
                   uri.AbsolutePath.Trim('/'),
                   out transactionId) &&
               transactionId != Guid.Empty;
    }
}

public interface IDeepLinkCoordinator
{
    Task<bool> HandleAsync(Uri uri);
    Task ResumePendingAsync();
}
