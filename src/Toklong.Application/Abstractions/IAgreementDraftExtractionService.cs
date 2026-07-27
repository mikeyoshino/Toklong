using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public sealed record AgreementDraftExtraction(
    string SellerPhoneNumber,
    string ProductName,
    string Description,
    string KnownDefects,
    decimal? PriceBaht,
    ConditionCode? Condition,
    string Confidence,
    IReadOnlyList<string> ExtractedFields);

public interface IAgreementDraftExtractionService
{
    Task<AgreementDraftExtraction> ExtractAsync(
        string chatText,
        IReadOnlyList<ListingImageInput> images,
        string safetyIdentifier,
        CancellationToken cancellationToken);
}
