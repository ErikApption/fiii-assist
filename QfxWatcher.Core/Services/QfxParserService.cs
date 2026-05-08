using QfxWatcher.Models;
using System.Text.RegularExpressions;

namespace QfxWatcher.Services;

/// <summary>
/// Parses QFX (OFX) files and returns a list of <see cref="FIIITransaction"/> objects.
/// Supports both legacy SGML-style OFX 1.x and XML-based OFX 2.x / QFX files.
/// </summary>
public static class QfxParserService
{
    // ── OFX date format: YYYYMMDDHHMMSS[.XXX][±ZZZ:ZZ] ─────────────────────
    private static readonly Regex DateRegex =
        new(@"^(\d{4})(\d{2})(\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// Parses the given file path and returns all transactions found.
    /// </summary>
    public static IReadOnlyList<FIIITransaction> ParseFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        return Parse(text);
    }

    /// <summary>
    /// Parses raw OFX/QFX content.
    /// </summary>
    public static IReadOnlyList<FIIITransaction> Parse(string content)
    {
        // Detect XML vs SGML
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<OFX", StringComparison.OrdinalIgnoreCase))
        {
            return ParseXml(trimmed);
        }

        return ParseSgml(content);
    }

    // ── SGML (OFX 1.x / QFX) parser ─────────────────────────────────────────

    private static IReadOnlyList<FIIITransaction> ParseSgml(string content)
    {
        // Strip header lines (everything before <OFX>)
        var ofxStart = content.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (ofxStart >= 0)
            content = content[ofxStart..];

        var transactions = new List<FIIITransaction>();

        // Find all <STMTTRN>…</STMTTRN> blocks
        var blockPattern = new Regex(
            @"<STMTTRN>(.*?)</STMTTRN>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match block in blockPattern.Matches(content))
        {
            var body = block.Groups[1].Value;
            transactions.Add(new FIIITransaction
            {
                FitId           = ReadSgmlTag(body, "FITID"),
                TransactionType = ReadSgmlTag(body, "TRNTYPE"),
                Date            = ParseOFXDate(ReadSgmlTag(body, "DTPOSTED")),
                Amount          = ParseAmount(ReadSgmlTag(body, "TRNAMT")),
                Name            = ReadSgmlTag(body, "NAME"),
                Memo            = ReadSgmlTag(body, "MEMO"),
            });
        }

        return transactions;
    }

    private static string ReadSgmlTag(string body, string tag)
    {
        // SGML tags may or may not be closed: <TAG>value or <TAG>value</TAG>
        var pattern = $@"<{tag}>\s*([^\r\n<]+)";
        var match = Regex.Match(body, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    // ── XML (OFX 2.x) parser ─────────────────────────────────────────────────

    private static IReadOnlyList<FIIITransaction> ParseXml(string content)
    {
        var transactions = new List<FIIITransaction>();

        // Strip XML declaration to avoid encoding issues
        var xmlStart = content.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
        if (xmlStart < 0) return transactions;
        content = content[xmlStart..];

        // Find all <STMTTRN>…</STMTTRN> blocks
        var blockPattern = new Regex(
            @"<STMTTRN>(.*?)</STMTTRN>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match block in blockPattern.Matches(content))
        {
            var body = block.Groups[1].Value;
            transactions.Add(new FIIITransaction
            {
                FitId           = ReadXmlTag(body, "FITID"),
                TransactionType = ReadXmlTag(body, "TRNTYPE"),
                Date            = ParseOFXDate(ReadXmlTag(body, "DTPOSTED")),
                Amount          = ParseAmount(ReadXmlTag(body, "TRNAMT")),
                Name            = ReadXmlTag(body, "NAME"),
                Memo            = ReadXmlTag(body, "MEMO"),
            });
        }

        return transactions;
    }

    private static string ReadXmlTag(string body, string tag)
    {
        var pattern = $@"<{tag}>\s*(.*?)\s*</{tag}>";
        var match = Regex.Match(body, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DateOnly ParseOFXDate(string raw)
    {
        var m = DateRegex.Match(raw);
        if (!m.Success) return DateOnly.FromDateTime(DateTime.UtcNow);
        return new DateOnly(int.Parse(m.Groups[1].Value),
                            int.Parse(m.Groups[2].Value),
                            int.Parse(m.Groups[3].Value));
    }

    private static decimal ParseAmount(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        raw = raw.Replace(",", ".");
        return decimal.TryParse(raw,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value) ? value : 0m;
    }
}
