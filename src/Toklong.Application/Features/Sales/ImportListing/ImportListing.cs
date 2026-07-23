using MediatR;
using Toklong.Application.Abstractions;

namespace Toklong.Application.Features.Sales.ImportListing;

public sealed record ImportListingCommand(string SourceUrl) : IRequest<ImportedListingDraft>;

public sealed class ImportListingHandler(IListingImportService importer)
    : IRequestHandler<ImportListingCommand, ImportedListingDraft>
{
    public Task<ImportedListingDraft> Handle(
        ImportListingCommand request,
        CancellationToken cancellationToken)
    {
        if (!PublicListingUrl.TryParse(request.SourceUrl, out var sourceUrl))
            throw new ArgumentException("กรุณาใส่ลิงก์ประกาศสาธารณะที่ขึ้นต้นด้วย http:// หรือ https://");

        return importer.ImportAsync(sourceUrl!, cancellationToken);
    }
}
