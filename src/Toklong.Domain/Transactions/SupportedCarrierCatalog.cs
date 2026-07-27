using System.Text.RegularExpressions;
using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public sealed record SupportedCarrier(
    string Code,
    string DisplayName,
    string TrackingHint,
    string TrackingExample,
    string ValidationPattern,
    string ValidationMessage,
    int MaximumLength)
{
    public bool IsValidTrackingNumber(string? value)
    {
        var normalized = SupportedCarrierCatalog.NormalizeTracking(value);
        return Regex.IsMatch(
            normalized,
            ValidationPattern,
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}

public static class SupportedCarrierCatalog
{
    private static readonly IReadOnlyList<SupportedCarrier> Items =
    [
        new(
            "THAIPOST",
            "ไปรษณีย์ไทย",
            "ตัวอักษร 2 ตัว ตามด้วยตัวเลข 9 ตัว และลงท้าย TH",
            "EF123456789TH",
            "^[A-Z]{2}[0-9]{9}TH$",
            "เลขไปรษณีย์ไทยต้องมี 13 ตัว เช่น EF123456789TH",
            13),
        new(
            "FLASH",
            "Flash Express",
            "ใช้เลขพัสดุบนใบรับฝากหรือใบปะหน้า",
            "TH1234567890",
            "^[A-Z0-9]{10,20}$",
            "เลข Flash ต้องเป็นตัวอักษรหรือตัวเลข 10–20 ตัว",
            20),
        new(
            "KERRY",
            "KEX Express (Kerry)",
            "ใช้เลขพัสดุบนใบรับฝากหรือใบปะหน้า",
            "KEX123456789",
            "^[A-Z0-9]{10,20}$",
            "เลข KEX ต้องเป็นตัวอักษรหรือตัวเลข 10–20 ตัว",
            20)
    ];

    public static IReadOnlyList<SupportedCarrier> All => Items;

    public static SupportedCarrier? Find(string? code)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        return Items.SingleOrDefault(item => item.Code == normalized);
    }

    public static string NormalizeTracking(string? value) =>
        new((value ?? "")
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    public static SupportedCarrier RequireValid(
        string? carrierCode,
        string? trackingNumber)
    {
        var carrier = Find(carrierCode)
            ?? throw new DomainException(
                "กรุณาเลือกบริษัทขนส่งที่ระบบรองรับ");
        if (!carrier.IsValidTrackingNumber(trackingNumber))
            throw new DomainException(carrier.ValidationMessage);
        return carrier;
    }
}
