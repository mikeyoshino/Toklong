namespace Toklong.Application.Abstractions;

public sealed record EmailVerificationCodePair(
    string Code,
    string Digest);

public interface IEmailVerificationCodeService
{
    EmailVerificationCodePair Issue(Guid challengeId);
    string Digest(Guid challengeId, string code);
    string HashDestination(string normalizedEmail);
}

public sealed record RenderedEmail(
    string Subject,
    string TextBody,
    string HtmlBody);

public interface IEmailVerificationTemplate
{
    RenderedEmail Render(string code);
}

public sealed record TransactionalEmailMessage(
    string Recipient,
    string Subject,
    string TextBody,
    string HtmlBody,
    string Purpose,
    string CorrelationId,
    string IdempotencyKey);

public sealed record EmailSendAcceptance(string ProviderReference);

public enum TransactionalEmailFailureKind
{
    Transient,
    Permanent
}

public sealed class TransactionalEmailSendException(
    string message,
    TransactionalEmailFailureKind kind) : Exception(message)
{
    public TransactionalEmailFailureKind Kind { get; } = kind;
}

public interface ITransactionalEmailSender
{
    Task<EmailSendAcceptance> SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken);
}

public interface IDevelopmentEmailInbox
{
    IReadOnlyList<TransactionalEmailMessage> Messages { get; }
}
