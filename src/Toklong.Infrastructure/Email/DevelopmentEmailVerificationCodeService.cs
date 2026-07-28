using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Email;

public sealed class DevelopmentEmailVerificationCodeService
    : IEmailVerificationCodeService
{
    private const string DevelopmentCode = "123456";
    private readonly HmacEmailVerificationCodeService _secureService;

    public DevelopmentEmailVerificationCodeService(
        EmailVerificationOptions options,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                "Development email verification codes are available only in Development or Testing");

        _secureService =
            new HmacEmailVerificationCodeService(options);
    }

    public EmailVerificationCodePair Issue(Guid challengeId) =>
        new(
            DevelopmentCode,
            Digest(challengeId, DevelopmentCode));

    public string Digest(Guid challengeId, string code) =>
        _secureService.Digest(challengeId, code);

    public string HashDestination(string normalizedEmail) =>
        _secureService.HashDestination(normalizedEmail);
}
