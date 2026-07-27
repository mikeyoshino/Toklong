namespace Toklong.Mobile.Core;

public sealed record BankOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public static class ThaiBankCatalog
{
    public static IReadOnlyList<BankOption> Supported { get; } =
    [
        new("BBL", "กรุงเทพ"),
        new("KBANK", "กสิกรไทย"),
        new("KTB", "กรุงไทย"),
        new("SCB", "ไทยพาณิชย์"),
        new("BAY", "กรุงศรีอยุธยา"),
        new("TTB", "ทีเอ็มบีธนชาต"),
        new("GSB", "ออมสิน")
    ];
}
