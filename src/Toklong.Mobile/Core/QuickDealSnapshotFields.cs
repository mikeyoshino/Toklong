namespace Toklong.Mobile.Core;

public sealed record QuickDealSnapshotFields(
    string Description,
    string KnownDefects);

public static class QuickDealSnapshotComposer
{
    public const string NoBuyerReportedDefects =
        "ไม่มีตำหนิที่ผู้ซื้อระบุ";

    public static QuickDealSnapshotFields Compose(
        string productName,
        string? optionalDetails,
        AppCondition condition,
        string? reportedDefects)
    {
        var cleanProductName = productName.Trim();
        var description = string.IsNullOrWhiteSpace(optionalDetails)
            ? cleanProductName
            : optionalDetails.Trim();
        var knownDefects = condition == AppCondition.UsedDefects
            ? reportedDefects?.Trim() ?? ""
            : NoBuyerReportedDefects;

        return new QuickDealSnapshotFields(
            description,
            knownDefects);
    }
}
