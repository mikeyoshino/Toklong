namespace Toklong.Application.Abstractions;

public sealed record DisputeEvidenceFileInput(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record StoredDisputeEvidenceFile(
    string StorageReference,
    string ContentType,
    long LengthBytes,
    string Sha256);

public sealed record DisputeEvidenceFileContent(
    byte[] Content,
    string ContentType);

public interface IDisputeEvidenceStore
{
    Task<StoredDisputeEvidenceFile> SaveImageAsync(
        DisputeEvidenceFileInput input,
        CancellationToken cancellationToken);

    Task<DisputeEvidenceFileContent> ReadAsync(
        string storageReference,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string storageReference,
        CancellationToken cancellationToken);
}
