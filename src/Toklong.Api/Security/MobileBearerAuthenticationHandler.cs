using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Toklong.Api.Security;

public sealed class MobileBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    MobileSessionTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization,
                out var header) ||
            !string.Equals(
                header.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
            return AuthenticateResult.NoResult();

        var principal = await tokens.ValidateAccessAsync(
            header.Parameter,
            Context.RequestAborted);
        return principal is null
            ? AuthenticateResult.Fail("เซสชันหมดอายุ")
            : AuthenticateResult.Success(
                new AuthenticationTicket(
                    principal,
                    MobileAuthenticationDefaults.Scheme));
    }
}
