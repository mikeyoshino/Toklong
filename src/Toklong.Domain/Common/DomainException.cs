namespace Toklong.Domain.Common;

public sealed class DomainException(string message) : InvalidOperationException(message);
