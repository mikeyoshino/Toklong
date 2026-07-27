using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Transactions.ManageDisputeEvidence;

public sealed record SubmitDisputeEvidenceCommand(
    Guid TransactionId,
    Guid? BuyerId,
    Guid? SellerId,
    DisputeEvidenceParty Party,
    DisputeEvidenceType EvidenceType,
    string Description,
    string IdempotencyKey,
    DisputeEvidenceFileInput File)
    : IRequest<DisputeEvidenceView>;

public sealed record ListOwnDisputeEvidenceQuery(
    Guid TransactionId,
    Guid? BuyerId,
    Guid? SellerId,
    DisputeEvidenceParty Party)
    : IRequest<IReadOnlyList<DisputeEvidenceView>>;

public sealed record GetOwnDisputeEvidenceFileQuery(
    Guid TransactionId,
    Guid EvidenceId,
    Guid? BuyerId,
    Guid? SellerId,
    DisputeEvidenceParty Party)
    : IRequest<DisputeEvidenceDownload>;

public sealed record NotifyDisputeEvidenceRequestCommand(
    Guid TransactionId,
    Guid RequestId,
    DisputeEvidenceParty Party,
    Guid RequestedByUserId,
    string RequiredEvidence,
    DateTimeOffset DueAt)
    : IRequest;

public sealed record DisputeEvidenceDownload(
    Guid EvidenceId,
    byte[] Content,
    string ContentType,
    string Sha256);

public sealed record DisputeEvidenceView(
    Guid Id,
    DisputeEvidenceParty Party,
    DisputeEvidenceType EvidenceType,
    string Description,
    string ContentType,
    long LengthBytes,
    string Sha256,
    DateTimeOffset SubmittedAt)
{
    public static DisputeEvidenceView From(
        DisputeEvidence evidence) =>
        new(
            evidence.Id,
            evidence.Party,
            evidence.EvidenceType,
            evidence.Description,
            evidence.ContentType,
            evidence.LengthBytes,
            evidence.Sha256,
            evidence.SubmittedAt);
}

public sealed class SubmitDisputeEvidenceHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock,
    IDisputeEvidenceStore evidenceStore)
    : IRequestHandler<
        SubmitDisputeEvidenceCommand,
        DisputeEvidenceView>
{
    public async Task<DisputeEvidenceView> Handle(
        SubmitDisputeEvidenceCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await GetAuthorizedAsync(
            repository,
            request.TransactionId,
            request.BuyerId,
            request.SellerId,
            request.Party,
            cancellationToken);
        var idempotencyKey = Required(
            request.IdempotencyKey,
            100,
            "Idempotency-Key");
        var existing = transaction.DisputeEvidence
            .SingleOrDefault(item =>
                item.Party == request.Party &&
                string.Equals(
                    item.IdempotencyKey,
                    idempotencyKey,
                    StringComparison.Ordinal));
        if (existing is not null)
            return DisputeEvidenceView.From(existing);
        if (request.File.Content.Length is < 1 or > 6_000_000)
            throw new DomainException(
                "รูปหลักฐานต้องมีขนาดไม่เกิน 6 MB");
        var description = ReusableCredentialGuard.Reject(
            Required(
                request.Description,
                1000,
                "คำอธิบายหลักฐาน"));

        StoredDisputeEvidenceFile? stored = null;
        try
        {
            stored = await evidenceStore.SaveImageAsync(
                request.File,
                cancellationToken);
            var submitterId = request.Party ==
                              DisputeEvidenceParty.Buyer
                ? request.BuyerId!.Value
                : request.SellerId!.Value;
            var evidence = transaction.RecordDisputeEvidence(
                Guid.NewGuid(),
                request.Party,
                submitterId,
                request.EvidenceType,
                description,
                stored.StorageReference,
                stored.ContentType,
                stored.LengthBytes,
                stored.Sha256,
                idempotencyKey,
                clock.UtcNow);
            await repository.AddDisputeEvidenceAsync(
                evidence,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DisputeEvidenceView.From(evidence);
        }
        catch
        {
            if (stored is not null)
                await evidenceStore.DeleteAsync(
                    stored.StorageReference,
                    CancellationToken.None);
            throw;
        }
    }

    private static string Required(
        string? value,
        int maximumLength,
        string label)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 ||
            clean.Length > maximumLength)
            throw new DomainException(
                $"{label}ไม่ถูกต้อง");
        return clean;
    }

    internal static async Task<SaleTransaction>
        GetAuthorizedAsync(
            ITransactionRepository repository,
            Guid transactionId,
            Guid? buyerId,
            Guid? sellerId,
            DisputeEvidenceParty party,
            CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            transactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        var authorized =
            party == DisputeEvidenceParty.Buyer
                ? buyerId.HasValue &&
                  transaction.BuyerId == buyerId.Value
                : sellerId.HasValue &&
                  transaction.SellerId == sellerId.Value;
        if (!authorized)
            throw new DomainException(
                "บัญชีนี้ไม่มีสิทธิ์ส่งหรือเปิดหลักฐานของฝ่ายนี้");
        return transaction;
    }
}

public sealed class ListOwnDisputeEvidenceHandler(
    ITransactionRepository repository)
    : IRequestHandler<
        ListOwnDisputeEvidenceQuery,
        IReadOnlyList<DisputeEvidenceView>>
{
    public async Task<IReadOnlyList<DisputeEvidenceView>> Handle(
        ListOwnDisputeEvidenceQuery request,
        CancellationToken cancellationToken)
    {
        var transaction =
            await SubmitDisputeEvidenceHandler
                .GetAuthorizedAsync(
                    repository,
                    request.TransactionId,
                    request.BuyerId,
                    request.SellerId,
                    request.Party,
                    cancellationToken);
        return transaction.DisputeEvidence
            .Where(item => item.Party == request.Party)
            .OrderByDescending(item => item.SubmittedAt)
            .Select(DisputeEvidenceView.From)
            .ToList();
    }
}

public sealed class GetOwnDisputeEvidenceFileHandler(
    ITransactionRepository repository,
    IDisputeEvidenceStore evidenceStore)
    : IRequestHandler<
        GetOwnDisputeEvidenceFileQuery,
        DisputeEvidenceDownload>
{
    public async Task<DisputeEvidenceDownload> Handle(
        GetOwnDisputeEvidenceFileQuery request,
        CancellationToken cancellationToken)
    {
        var transaction =
            await SubmitDisputeEvidenceHandler
                .GetAuthorizedAsync(
                    repository,
                    request.TransactionId,
                    request.BuyerId,
                    request.SellerId,
                    request.Party,
                    cancellationToken);
        var evidence = transaction.DisputeEvidence
            .SingleOrDefault(item =>
                item.Id == request.EvidenceId &&
                item.Party == request.Party)
            ?? throw new NotFoundException(
                "ไม่พบหลักฐาน");
        var file = await evidenceStore.ReadAsync(
            evidence.StorageReference,
            cancellationToken);
        var actualHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256
                    .HashData(file.Content))
            .ToLowerInvariant();
        if (!System.Security.Cryptography
                .CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(evidence.Sha256),
                    Convert.FromHexString(actualHash)))
            throw new InvalidOperationException(
                "การตรวจสอบความสมบูรณ์ของหลักฐานล้มเหลว");
        return new DisputeEvidenceDownload(
            evidence.Id,
            file.Content,
            file.ContentType,
            actualHash);
    }
}

public sealed class NotifyDisputeEvidenceRequestHandler(
    ITransactionRepository repository,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<NotifyDisputeEvidenceRequestCommand>
{
    public async Task Handle(
        NotifyDisputeEvidenceRequestCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        var changed = transaction.RequestDisputeEvidence(
            request.RequestId,
            request.Party,
            request.RequestedByUserId,
            request.RequiredEvidence,
            request.DueAt,
            clock.UtcNow);
        if (changed)
            await unitOfWork.SaveChangesAsync(
                cancellationToken);
    }
}
