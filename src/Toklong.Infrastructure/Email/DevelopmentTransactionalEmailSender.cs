using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Email;

public sealed class DevelopmentTransactionalEmailSender
    : ITransactionalEmailSender, IDevelopmentEmailInbox
{
    private const int MaximumMessages = 50;
    private readonly object _sync = new();
    private readonly Queue<TransactionalEmailMessage> _messages = new();

    public DevelopmentTransactionalEmailSender(
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                "Development email delivery is available only in Development or Testing");
    }

    public IReadOnlyList<TransactionalEmailMessage> Messages
    {
        get
        {
            lock (_sync)
                return _messages.ToArray();
        }
    }

    public Task<EmailSendAcceptance> SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _messages.Enqueue(message);
            while (_messages.Count > MaximumMessages)
                _messages.Dequeue();
        }

        return Task.FromResult(
            new EmailSendAcceptance(
                $"dev-email-{message.CorrelationId}"));
    }
}
