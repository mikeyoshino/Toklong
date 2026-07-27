using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Toklong.Crm.Authentication;

public sealed class CrmCookiePostConfigureOptions(
    CrmTicketStore ticketStore)
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(
        string? name,
        CookieAuthenticationOptions options)
    {
        if (string.Equals(
                name,
                CrmAuthenticationDefaults.CookieScheme,
                StringComparison.Ordinal))
            options.SessionStore = ticketStore;
    }
}
