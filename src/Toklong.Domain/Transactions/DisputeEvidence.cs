namespace Toklong.Domain.Transactions;

public enum DisputeEvidenceParty
{
    Buyer,
    Seller
}

public enum DisputeEvidenceType
{
    Item,
    Packaging,
    ShippingLabel,
    SerialOrIdentifier,
    ReceiptOrProvenance,
    HandoffRecord,
    Other
}

public sealed class DisputeEvidence
{
    private DisputeEvidence() { }

    internal DisputeEvidence(
        Guid id,
        Guid transactionId,
        DisputeEvidenceParty party,
        Guid submittedById,
        DisputeEvidenceType evidenceType,
        string description,
        string storageReference,
        string contentType,
        long lengthBytes,
        string sha256,
        string idempotencyKey,
        DateTimeOffset submittedAt)
    {
        Id = id;
        TransactionId = transactionId;
        Party = party;
        SubmittedById = submittedById;
        EvidenceType = evidenceType;
        Description = description;
        StorageReference = storageReference;
        ContentType = contentType;
        LengthBytes = lengthBytes;
        Sha256 = sha256;
        IdempotencyKey = idempotencyKey;
        SubmittedAt = submittedAt;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public DisputeEvidenceParty Party { get; private set; }
    public Guid SubmittedById { get; private set; }
    public DisputeEvidenceType EvidenceType { get; private set; }
    public string Description { get; private set; } = "";
    public string StorageReference { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long LengthBytes { get; private set; }
    public string Sha256 { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
    public DateTimeOffset SubmittedAt { get; private set; }
}
