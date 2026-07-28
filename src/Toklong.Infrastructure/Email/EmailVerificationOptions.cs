using Microsoft.Extensions.Configuration;

namespace Toklong.Infrastructure.Email;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public string Provider { get; init; } = "Unavailable";
    public string DigestKey { get; init; } = "";
    public string BrandLogoUrl { get; init; } = "";

    public static EmailVerificationOptions From(
        IConfiguration configuration) =>
        new()
        {
            Provider =
                configuration[$"{SectionName}:Provider"] ??
                "Unavailable",
            DigestKey =
                configuration[$"{SectionName}:DigestKey"] ?? "",
            BrandLogoUrl =
                configuration[$"{SectionName}:BrandLogoUrl"] ?? ""
        };
}
