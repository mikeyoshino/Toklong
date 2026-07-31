using System.Globalization;
using System.Text;
using Toklong.Domain.Common;

namespace Toklong.Domain.Accounts;

public sealed record AccountName
{
    private const int MaximumPartLength = 60;
    private const int MaximumDisplayNameLength = 120;

    private AccountName(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    public string FirstName { get; }
    public string LastName { get; }
    public string DisplayName => $"{FirstName} {LastName}";

    public static AccountName Create(string firstName, string lastName)
    {
        var normalizedFirstName = NormalizePart(firstName, "ชื่อ");
        var normalizedLastName = NormalizePart(lastName, "นามสกุล");
        EnsureDisplayNameLength(normalizedFirstName, normalizedLastName);
        return new AccountName(normalizedFirstName, normalizedLastName);
    }

    // Existing rows predate per-part limits. This path is assembly-only so it
    // can be used while materializing those rows without accepting it as input.
    internal static AccountName MaterializeLegacy(string firstName, string lastName)
    {
        var normalizedFirstName = NormalizeLegacyPart(firstName, "ชื่อ");
        var normalizedLastName = NormalizeLegacyPart(lastName, "นามสกุล");
        EnsureDisplayNameLength(normalizedFirstName, normalizedLastName);
        return new AccountName(normalizedFirstName, normalizedLastName);
    }

    internal static AccountName MaterializeLegacyDisplayName(string displayName)
    {
        var normalized = CollapseWhitespace(displayName);
        var separator = normalized.IndexOf(' ');
        if (separator <= 0 || separator == normalized.Length - 1)
            throw new DomainException("กรุณากรอกชื่อและนามสกุล");

        return MaterializeLegacy(
            normalized[..separator],
            normalized[(separator + 1)..]);
    }

    internal static AccountName CreateFromDisplayName(string displayName)
    {
        var normalized = CollapseWhitespace(displayName);
        var separator = normalized.LastIndexOf(' ');
        if (separator <= 0 || separator == normalized.Length - 1)
            throw new DomainException("กรุณากรอกชื่อและนามสกุล");

        return Create(
            normalized[..separator],
            normalized[(separator + 1)..]);
    }

    private static string NormalizePart(string value, string label)
    {
        var normalized = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainException($"กรุณากรอก{label}");
        if (normalized.Length > MaximumPartLength)
            throw new DomainException($"{label}ยาวเกิน {MaximumPartLength} ตัวอักษร");

        var hasLetter = false;
        var previousWasSeparator = true;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (IsSupportedLetter(rune))
            {
                hasLetter = true;
                previousWasSeparator = false;
                continue;
            }

            if (IsSupportedMark(rune))
            {
                if (previousWasSeparator)
                    throw new DomainException($"{label}มีอักขระที่ไม่รองรับ");
                continue;
            }

            if (IsSeparator(rune))
            {
                if (previousWasSeparator)
                    throw new DomainException($"{label}มีอักขระที่ไม่รองรับ");
                previousWasSeparator = true;
                continue;
            }

            throw new DomainException($"{label}มีอักขระที่ไม่รองรับ");
        }

        if (!hasLetter || previousWasSeparator)
            throw new DomainException($"{label}มีอักขระที่ไม่รองรับ");

        return normalized;
    }

    private static string NormalizeLegacyPart(string value, string label)
    {
        var normalized = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new DomainException($"กรุณากรอก{label}");
        return normalized;
    }

    private static void EnsureDisplayNameLength(string firstName, string lastName)
    {
        if (firstName.Length + 1 + lastName.Length > MaximumDisplayNameLength)
            throw new DomainException(
                $"ชื่อและนามสกุลยาวเกิน {MaximumDisplayNameLength} ตัวอักษร");
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(
            ' ',
            (value ?? "").Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

    private static bool IsSupportedLetter(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter;

    private static bool IsSupportedMark(Rune rune) =>
        Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark;

    private static bool IsSeparator(Rune rune) =>
        rune.Value is ' ' or '-' or '\'' or 0x2019;
}
