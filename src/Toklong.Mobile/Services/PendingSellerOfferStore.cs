using Microsoft.Maui.Storage;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class PendingSellerOfferStore : IPendingSellerOfferStore
{
    private const string OfferKey = "toklong.pending_seller_offer";
    private const string TransactionKey =
        "toklong.pending_transaction";

    public string? PendingToken =>
        Preferences.Default.Get<string?>(OfferKey, null);

    public Guid? PendingTransactionId
    {
        get
        {
            var raw = Preferences.Default.Get<string?>(
                TransactionKey,
                null);
            return Guid.TryParse(raw, out var transactionId)
                ? transactionId
                : null;
        }
    }

    public void Save(string publicToken)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            return;
        Preferences.Default.Remove(TransactionKey);
        Preferences.Default.Set(OfferKey, publicToken.Trim());
    }

    public void SaveTransaction(Guid transactionId)
    {
        if (transactionId == Guid.Empty)
            return;
        Preferences.Default.Remove(OfferKey);
        Preferences.Default.Set(
            TransactionKey,
            transactionId.ToString("D"));
    }

    public string? Take()
    {
        var value = PendingToken;
        Preferences.Default.Remove(OfferKey);
        return value;
    }

    public Guid? TakeTransaction()
    {
        var value = PendingTransactionId;
        Preferences.Default.Remove(TransactionKey);
        return value;
    }
}
