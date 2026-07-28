using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Email;

namespace Toklong.Application.Tests.Email;

public sealed class EmailVerificationDeliveryTests
{
    private static readonly Guid ChallengeId =
        Guid.Parse("7d7f2921-c51a-450c-a18d-3b2f1f461c06");

    [Fact]
    public void Development_code_is_fixed_but_only_digest_is_persistable()
    {
        var service = DevelopmentCodeService();

        var pair = service.Issue(ChallengeId);

        Assert.Equal("123456", pair.Code);
        Assert.Equal(64, pair.Digest.Length);
        Assert.NotEqual(pair.Code, pair.Digest);
        Assert.Equal(
            pair.Digest,
            service.Digest(ChallengeId, "123456"));
        Assert.NotEqual(
            pair.Digest,
            service.Digest(Guid.NewGuid(), "123456"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Development_code_service_allows_only_test_environments(
        string environmentName)
    {
        var service = CreateDevelopmentCodeService(environmentName);

        Assert.Equal("123456", service.Issue(ChallengeId).Code);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Development_code_service_rejects_non_test_environments(
        string environmentName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateDevelopmentCodeService(environmentName));
    }

    [Fact]
    public void Secure_code_is_six_digits_and_uses_challenge_bound_hmac()
    {
        var service = SecureCodeService();

        var pair = service.Issue(ChallengeId);

        Assert.Matches("^[0-9]{6}$", pair.Code);
        Assert.Equal(64, pair.Digest.Length);
        Assert.Equal(
            pair.Digest,
            service.Digest(ChallengeId, pair.Code));
        Assert.NotEqual(
            pair.Digest,
            service.Digest(Guid.NewGuid(), pair.Code));
    }

    [Fact]
    public void Privacy_hash_is_keyed_deterministic_and_does_not_expose_email()
    {
        var service = SecureCodeService();

        var hash = service.HashDestination("buyer@example.com");

        Assert.Equal(64, hash.Length);
        Assert.Equal(
            hash,
            service.HashDestination("buyer@example.com"));
        Assert.DoesNotContain("buyer", hash, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            hash,
            service.HashDestination("other@example.com"));
    }

    [Fact]
    public void Digest_key_must_have_at_least_32_utf8_bytes()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new HmacEmailVerificationCodeService(
                Options(digestKey: new string('k', 31))));

        Assert.Contains(
            "EmailVerification:DigestKey",
            exception.Message);
    }

    [Fact]
    public void Thai_template_is_responsive_escaped_and_complete_without_image()
    {
        var message = Template().Render("123456");

        Assert.Equal(
            "รหัสยืนยันอีเมลใหม่ของคุณจาก TOKLONG",
            message.Subject);
        Assert.Contains("role=\"presentation\"", message.HtmlBody);
        Assert.Contains("max-width:600px", message.HtmlBody);
        Assert.Contains("width=\"100%\"", message.HtmlBody);
        Assert.Contains("alt=\"TOKLONG\"", message.HtmlBody);
        Assert.Contains(">TOKLONG<", message.HtmlBody);
        Assert.Contains("ยืนยันอีเมลใหม่ของคุณ", message.HtmlBody);
        Assert.Contains(
            "กรอกรหัสนี้ในแอป TOKLONG เพื่อยืนยันการเปลี่ยนอีเมล",
            message.HtmlBody);
        Assert.Contains("123 456", message.HtmlBody);
        Assert.Contains("123456", message.TextBody);
        Assert.Contains("10 นาที", message.HtmlBody);
        Assert.Contains("10 นาที", message.TextBody);
        Assert.Contains(
            "ห้ามบอกรหัสนี้กับผู้อื่น",
            message.HtmlBody);
        Assert.Contains(
            "ข้อมูลบัญชีธนาคาร",
            message.TextBody);
        Assert.DoesNotContain("<script", message.HtmlBody);
    }

    [Fact]
    public void Thai_template_escapes_dynamic_logo_and_code_values()
    {
        var template = new ToklongEmailVerificationTemplate(
            Options(
                brandLogoUrl:
                "https://assets.example/logo.png\" onerror=\"alert(1)"));

        var message = template.Render("123<script>");

        Assert.DoesNotContain("onerror=\"alert(1)\"", message.HtmlBody);
        Assert.Contains("&quot; onerror=&quot;", message.HtmlBody);
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
    }

    [Fact]
    public async Task Development_sender_captures_without_logging_or_http()
    {
        var sender = new DevelopmentTransactionalEmailSender(
            new TestEnvironment("Testing"));

        var acceptance = await sender.SendAsync(Message("correlation-1"), default);

        Assert.Equal(
            "dev-email-correlation-1",
            acceptance.ProviderReference);
        Assert.Single(sender.Messages);
        Assert.Equal(
            "buyer@example.com",
            sender.Messages[0].Recipient);
    }

    [Fact]
    public async Task Development_sender_keeps_only_newest_50_messages()
    {
        var sender = new DevelopmentTransactionalEmailSender(
            new TestEnvironment("Development"));

        for (var index = 0; index < 55; index++)
            await sender.SendAsync(Message($"correlation-{index}"), default);

        Assert.Equal(50, sender.Messages.Count);
        Assert.Equal(
            "correlation-5",
            sender.Messages[0].CorrelationId);
        Assert.Equal(
            "correlation-54",
            sender.Messages[^1].CorrelationId);
    }

    [Fact]
    public void Development_sender_rejects_production()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new DevelopmentTransactionalEmailSender(
                new TestEnvironment("Production")));
    }

    [Fact]
    public async Task Unavailable_sender_throws_typed_plain_language_failure()
    {
        var sender = new UnavailableTransactionalEmailSender();

        var exception =
            await Assert.ThrowsAsync<TransactionalEmailSendException>(() =>
                sender.SendAsync(Message("correlation-1"), default));

        Assert.Equal(
            TransactionalEmailFailureKind.Transient,
            exception.Kind);
        Assert.Equal(
            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง",
            exception.Message);
    }

    [Fact]
    public void Development_dependency_injection_exposes_same_bounded_inbox()
    {
        using var provider = Services(
                "Development",
                new Dictionary<string, string?>
                {
                    ["EmailVerification:Provider"] = "Development",
                    ["EmailVerification:DigestKey"] =
                        "development-email-digest-key-at-least-32-characters",
                    ["EmailVerification:BrandLogoUrl"] =
                        "https://assets.toklong.co.th/email/transaction-rail.png"
                })
            .BuildServiceProvider();

        var sender = provider.GetRequiredService<ITransactionalEmailSender>();
        var inbox = provider.GetRequiredService<IDevelopmentEmailInbox>();

        Assert.Same(sender, inbox);
        Assert.IsType<DevelopmentEmailVerificationCodeService>(
            provider.GetRequiredService<IEmailVerificationCodeService>());
        Assert.IsType<ToklongEmailVerificationTemplate>(
            provider.GetRequiredService<IEmailVerificationTemplate>());
    }

    [Fact]
    public void Unavailable_dependency_injection_uses_secure_code_service()
    {
        using var provider = Services(
                "Production",
                new Dictionary<string, string?>
                {
                    ["EmailVerification:Provider"] = "Unavailable",
                    ["EmailVerification:DigestKey"] =
                        "production-shaped-test-key-at-least-32-characters",
                    ["EmailVerification:BrandLogoUrl"] =
                        "https://assets.toklong.co.th/email/transaction-rail.png"
                })
            .BuildServiceProvider();

        Assert.IsType<UnavailableTransactionalEmailSender>(
            provider.GetRequiredService<ITransactionalEmailSender>());
        Assert.IsType<HmacEmailVerificationCodeService>(
            provider.GetRequiredService<IEmailVerificationCodeService>());
        Assert.Null(provider.GetService<IDevelopmentEmailInbox>());
    }

    private static DevelopmentEmailVerificationCodeService
        DevelopmentCodeService() =>
        CreateDevelopmentCodeService("Development");

    private static DevelopmentEmailVerificationCodeService
        CreateDevelopmentCodeService(string environmentName) =>
        new(
            Options(),
            new TestEnvironment(environmentName));

    private static HmacEmailVerificationCodeService SecureCodeService() =>
        new(Options());

    private static ToklongEmailVerificationTemplate Template() =>
        new(Options());

    private static EmailVerificationOptions Options(
        string digestKey =
            "email-verification-test-key-at-least-32-characters",
        string brandLogoUrl =
            "https://assets.toklong.co.th/email/transaction-rail.png") =>
        new()
        {
            Provider = "Development",
            DigestKey = digestKey,
            BrandLogoUrl = brandLogoUrl
        };

    private static TransactionalEmailMessage Message(string correlationId) =>
        new(
            "buyer@example.com",
            "subject",
            "text",
            "<p>html</p>",
            "buyer-email-change-verification",
            correlationId,
            $"idempotency-{correlationId}");

    private static ServiceCollection Services(
        string environmentName,
        IReadOnlyDictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestEnvironment(environmentName));
        services.AddInfrastructure(
            new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build());
        return services;
    }

    private sealed class TestEnvironment(string name)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } =
            Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
