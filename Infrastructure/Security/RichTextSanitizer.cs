using System.Net;
using System.Text.RegularExpressions;

namespace ForoAttritionErrorWeb.Infrastructure.Security;

public static class RichTextSanitizer
{
    // This is a minimal allow-list sanitizer designed for content produced by our own editor toolbar.
    // Allowed tags: b/strong, i/em, u, p, br, ul/ol/li, div, span (only color style).
    // Everything else is stripped; dangerous attributes are removed.

    private static readonly Regex ScriptStyleBlock = new(
        @"<(script|style)[^>]*?>[\s\S]*?<\/\1>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EventHandlers = new(
        @"\s+on[a-zA-Z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DangerousAttrs = new(
        @"\s+(href|src)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NotAllowedTags = new(
        @"</?(?!b\b|strong\b|i\b|em\b|u\b|p\b|br\b|ul\b|ol\b|li\b|span\b|div\b)[a-zA-Z0-9]+\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AllowedStartTag = new(
        @"<(b|strong|i|em|u|p|ul|ol|li|div|span)([^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ColorStyle = new(
        @"color\s*:\s*(#[0-9a-fA-F]{6}|#[0-9a-fA-F]{3}|[a-zA-Z]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var sanitized = html;

        // Remove script/style blocks entirely.
        sanitized = ScriptStyleBlock.Replace(sanitized, string.Empty);

        // Remove common dangerous attributes.
        sanitized = EventHandlers.Replace(sanitized, string.Empty);
        sanitized = DangerousAttrs.Replace(sanitized, string.Empty);

        // Remove disallowed tags while leaving their inner text.
        sanitized = NotAllowedTags.Replace(sanitized, string.Empty);

        // Normalize allowed start tags: strip all attrs except span color style.
        sanitized = AllowedStartTag.Replace(sanitized, match =>
        {
            var tag = match.Groups[1].Value.ToLowerInvariant();
            var attrs = match.Groups[2].Value;

            if (tag != "span")
            {
                return $"<{tag}>";
            }

            var styleMatch = Regex.Match(attrs, @"style\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!styleMatch.Success)
            {
                return "<span>";
            }

            var styleValue = styleMatch.Groups[2].Success ? styleMatch.Groups[2].Value : styleMatch.Groups[3].Value;
            var color = ColorStyle.Match(styleValue);
            if (!color.Success)
            {
                return "<span>";
            }

            return $"<span style=\"{WebUtility.HtmlEncode(color.Value)}\">";
        });

        // Convert empty divs to paragraphs for consistency.
        sanitized = sanitized.Replace("<div>", "<p>", StringComparison.OrdinalIgnoreCase)
                             .Replace("</div>", "</p>", StringComparison.OrdinalIgnoreCase);

        return sanitized.Trim();
    }

    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"</p\s*>", "\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }
}
