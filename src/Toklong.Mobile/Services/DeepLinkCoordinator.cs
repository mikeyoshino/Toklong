using Toklong.Mobile.Core;
using Toklong.Mobile.Pages;

namespace Toklong.Mobile.Services;

public sealed class DeepLinkCoordinator(
    IAuthenticationService authentication,
    IPendingSellerOfferStore pendingOffers) : IDeepLinkCoordinator
{
    public async Task<bool> HandleAsync(Uri uri)
    {
        if (SellerOfferLink.TryGetPublicToken(uri, out var token))
            pendingOffers.Save(token);
        else if (TransactionLink.TryGetTransactionId(
                     uri,
                     out var transactionId))
            pendingOffers.SaveTransaction(transactionId);
        else
            return false;

        await NavigateAsync();
        return true;
    }

    public Task ResumePendingAsync() => NavigateAsync();

    private async Task NavigateAsync()
    {
        if (Shell.Current is null ||
            (pendingOffers.PendingToken is null &&
             pendingOffers.PendingTransactionId is null))
            return;
        if (!await authentication.HasSessionAsync())
        {
            await Shell.Current.GoToAsync("//signin");
            return;
        }

        if (pendingOffers.PendingToken is not null)
        {
            var token = pendingOffers.Take();
            if (string.IsNullOrWhiteSpace(token))
                return;
            await Shell.Current.GoToAsync(
                nameof(SellerOfferPage),
                new Dictionary<string, object>
                {
                    ["PublicToken"] = token
                });
            return;
        }

        var id = pendingOffers.TakeTransaction();
        if (id is not null)
        {
            await Shell.Current.GoToAsync(
                nameof(TransactionDetailPage),
                new Dictionary<string, object>
                {
                    ["TransactionId"] = id.Value
                });
        }
    }
}
