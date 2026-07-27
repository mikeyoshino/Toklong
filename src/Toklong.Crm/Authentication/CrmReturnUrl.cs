namespace Toklong.Crm.Authentication;

public static class CrmReturnUrl
{
    public static string Safe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !Uri.TryCreate(
                returnUrl,
                UriKind.Relative,
                out var uri) ||
            !returnUrl.StartsWith(
                "/",
                StringComparison.Ordinal) ||
            returnUrl.StartsWith(
                "//",
                StringComparison.Ordinal) ||
            uri.IsAbsoluteUri)
            return "/";
        return returnUrl;
    }
}
