namespace Toklong.Mobile.Core;

public static class ThaiMobilePhoneInput
{
    public const int LocalNumberLength = 10;
    public const int FormattedLocalNumberLength = 12;

    public static string Sanitize(string? value) =>
        new((value ?? "")
            .Where(character => character is >= '0' and <= '9')
            .Take(LocalNumberLength)
            .ToArray());

    public static string Format(string? value)
    {
        var digits = Sanitize(value);
        return digits.Length switch
        {
            <= 3 => digits,
            <= 6 => $"{digits[..3]}-{digits[3..]}",
            _ => $"{digits[..3]}-{digits[3..6]}-{digits[6..]}"
        };
    }

    public static bool IsValid(string? value) =>
        new string((value ?? "")
            .Where(character => character is >= '0' and <= '9')
            .ToArray()) is
        [
            '0',
            '6' or '8' or '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9',
            >= '0' and <= '9'
        ];
}
