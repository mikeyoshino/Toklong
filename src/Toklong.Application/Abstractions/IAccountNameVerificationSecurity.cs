namespace Toklong.Application.Abstractions;

public interface IAccountNameVerificationSecurity
{
    string Digest(Guid challengeId, string code);
}
