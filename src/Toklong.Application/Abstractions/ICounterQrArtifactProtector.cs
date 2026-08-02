namespace Toklong.Application.Abstractions;

public sealed record CounterQrArtifact(
    byte[] Content,
    string ContentType);

public sealed record ProtectedCounterQrArtifact(
    byte[] Ciphertext,
    string ProtectionVersion,
    string Sha256);

public interface ICounterQrArtifactProtector
{
    ProtectedCounterQrArtifact Protect(
        CounterQrArtifact artifact);

    CounterQrArtifact Unprotect(
        ProtectedCounterQrArtifact artifact);
}
