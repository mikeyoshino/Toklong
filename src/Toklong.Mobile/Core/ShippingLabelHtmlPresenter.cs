using System.Text.RegularExpressions;

namespace Toklong.Mobile.Core;

public static partial class ShippingLabelHtmlPresenter
{
    private const string PreviewHead = """
        <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=5, user-scalable=yes">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: https:; style-src 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com">
        <style>
          html { min-height: 100%; background: #f6f9fc; }
          body { margin: 0 auto !important; background: #fff; }
          img, svg { max-width: 100%; height: auto; }
        </style>
        """;

    public static string PreparePreview(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException(
                "ใบปะหน้าที่ได้รับไม่มีข้อมูล");
        if (!html.Contains(
                "<html",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "รูปแบบใบปะหน้าไม่ถูกต้อง");

        var safe = ScriptElementRegex().Replace(
            html,
            "");
        safe = InlineEventRegex().Replace(
            safe,
            "");
        safe = JavascriptUrlRegex().Replace(
            safe,
            "$1=\"#\"");

        var head = HeadElementRegex().Match(safe);
        if (head.Success)
            return safe.Insert(
                head.Index + head.Length,
                PreviewHead);

        var htmlElement = HtmlElementRegex().Match(safe);
        if (!htmlElement.Success)
            throw new InvalidOperationException(
                "รูปแบบใบปะหน้าไม่ถูกต้อง");
        return safe.Insert(
            htmlElement.Index + htmlElement.Length,
            $"<head>{PreviewHead}</head>");
    }

    [GeneratedRegex(
        @"<script\b[^>]*>[\s\S]*?</script\s*>",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex ScriptElementRegex();

    [GeneratedRegex(
        @"\son[a-z][a-z0-9_-]*\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex InlineEventRegex();

    [GeneratedRegex(
        @"\b(href|src)\s*=\s*(?:""javascript:[^""]*""|'javascript:[^']*')",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex JavascriptUrlRegex();

    [GeneratedRegex(
        @"<head\b[^>]*>",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex HeadElementRegex();

    [GeneratedRegex(
        @"<html\b[^>]*>",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex HtmlElementRegex();
}
