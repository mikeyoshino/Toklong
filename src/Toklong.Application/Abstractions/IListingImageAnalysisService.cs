using Toklong.Domain.Transactions;

namespace Toklong.Application.Abstractions;

public sealed record ListingImageInput(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record ListingImageAnalysis(
    string ProductName,
    string Description,
    string KnownDefects,
    decimal? PriceBaht,
    string Category,
    ConditionCode Condition,
    string Confidence,
    IReadOnlyList<string> ExtractedFields);

public sealed record AnalyzedListingDraft(
    string ProductName,
    string Description,
    string KnownDefects,
    decimal? PriceBaht,
    string Category,
    ConditionCode Condition,
    string ProductPhotoPath,
    string Confidence,
    IReadOnlyList<string> ExtractedFields);

public interface IListingImageAnalysisService
{
    Task<ListingImageAnalysis> AnalyzeAsync(
        IReadOnlyList<ListingImageInput> images,
        CancellationToken cancellationToken);
}

public interface IImportedProductImageStore
{
    Task<string> SaveAsync(ListingImageInput image, CancellationToken cancellationToken);
}
