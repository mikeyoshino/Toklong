namespace Toklong.Mobile.Core;

public sealed record AgreementDraft(
    string SellerPhoneNumber,
    string ProductName,
    string Description,
    string KnownDefects,
    decimal? PriceBaht,
    AppCondition? Condition,
    string Confidence,
    IReadOnlyList<string> ExtractedFields)
{
    public int ExtractedFieldCount => ExtractedFields.Count;
}

public interface IAgreementDraftService
{
    Task<AgreementDraft> ExtractAsync(
        string chatText,
        IReadOnlyList<string> localImagePaths,
        CancellationToken cancellationToken = default);
}

public sealed record AgreementFormValues(
    string SellerPhoneNumber,
    string ProductName,
    string AgreementDetails,
    string KnownDefects,
    string AmountBaht,
    int SelectedConditionIndex);

public sealed record AgreementDraftMergeResult(
    AgreementFormValues Values,
    int AppliedFieldCount);

public static class AgreementDraftMerger
{
    public static AgreementDraftMergeResult MergeBlankFields(
        AgreementFormValues current,
        AgreementDraft draft)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(draft);
        var applied = 0;

        var sellerPhone = Fill(
            current.SellerPhoneNumber,
            draft.SellerPhoneNumber,
            ref applied);
        var productName = Fill(
            current.ProductName,
            draft.ProductName,
            ref applied);
        var details = Fill(
            current.AgreementDetails,
            draft.Description,
            ref applied);
        var defects = Fill(
            current.KnownDefects,
            draft.KnownDefects,
            ref applied);
        var amount = current.AmountBaht;
        if (string.IsNullOrWhiteSpace(amount) &&
            draft.PriceBaht is > 0)
        {
            amount = draft.PriceBaht.Value.ToString(
                "0.##",
                System.Globalization.CultureInfo.InvariantCulture);
            applied++;
        }

        var conditionIndex = current.SelectedConditionIndex;
        if (conditionIndex < 0 && draft.Condition.HasValue)
        {
            conditionIndex = draft.Condition.Value switch
            {
                AppCondition.New => 0,
                AppCondition.UsedGood => 1,
                AppCondition.UsedDefects => 2,
                _ => -1
            };
            if (conditionIndex >= 0)
                applied++;
        }

        return new AgreementDraftMergeResult(
            new AgreementFormValues(
                sellerPhone,
                productName,
                details,
                defects,
                amount,
                conditionIndex),
            applied);
    }

    private static string Fill(
        string current,
        string candidate,
        ref int applied)
    {
        if (!string.IsNullOrWhiteSpace(current) ||
            string.IsNullOrWhiteSpace(candidate))
            return current;
        applied++;
        return candidate.Trim();
    }
}
