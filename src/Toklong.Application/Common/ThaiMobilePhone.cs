namespace Toklong.Application.Common;

public static class ThaiMobilePhone
{
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Invalid();

        var compact = new string(
            value.Trim()
                .Where(character =>
                    character is not (' ' or '-' or '(' or ')'))
                .ToArray());

        if (compact.Length == 10 &&
            compact[0] == '0' &&
            IsMobilePrefix(compact[1]) &&
            compact.All(char.IsAsciiDigit))
            return $"+66{compact[1..]}";

        if (compact.Length == 12 &&
            compact.StartsWith("+66", StringComparison.Ordinal) &&
            IsMobilePrefix(compact[3]) &&
            compact[1..].All(char.IsAsciiDigit))
            return compact;

        if (compact.Length == 11 &&
            compact.StartsWith("66", StringComparison.Ordinal) &&
            IsMobilePrefix(compact[2]) &&
            compact.All(char.IsAsciiDigit))
            return $"+{compact}";

        throw Invalid();
    }

    private static bool IsMobilePrefix(char value) =>
        value is '6' or '8' or '9';

    private static ArgumentException Invalid() =>
        new("กรุณากรอกเบอร์มือถือไทย 10 หลัก เช่น 0812345678");
}
