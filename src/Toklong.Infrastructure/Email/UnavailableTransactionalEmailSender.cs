using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Email;

public sealed class UnavailableTransactionalEmailSender
    : ITransactionalEmailSender
{
    public Task<EmailSendAcceptance> SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken) =>
        throw new TransactionalEmailSendException(
            "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง",
            TransactionalEmailFailureKind.Transient);
}
