using FiiiAssist.Models;
using System.Text.RegularExpressions;

namespace FiiiAssist.Services;

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
    /// Extracts the account ID (ACCTID) from the BANKACCTFROM element in the statement header.
    /// This is the account the QFX file belongs to (the source account at the bank).
    /// Returns empty string if not found.
    /// </summary>
    public static string ExtractAccountId(string filePath)
    {
        var text = File.ReadAllText(filePath);
        return ExtractAccountIdFromContent(text);
    }

    /// <summary>
    /// Extracts the account ID (ACCTID) from raw OFX/QFX content.
    /// Looks in the BANKACCTFROM block within STMTRS (statement response).
    /// </summary>
    public static string ExtractAccountIdFromContent(string content)
    {
        // Look for BANKACCTFROM inside STMTRS (not inside individual STMTTRN blocks)
        // SGML style: <STMTRS>...<BANKACCTFROM>...<ACCTID>12345...
        // XML style:  <STMTRS>...<BANKACCTFROM><ACCTID>12345</ACCTID>...

        // Find STMTRS block first to avoid picking up BANKACCTFROM from transactions
        var stmtrsPattern = new Regex(
            @"<STMTRS>(.*?)<BANKTRANLIST>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var stmtrsMatch = stmtrsPattern.Match(content);

        string searchArea;
        if (stmtrsMatch.Success)
        {
            searchArea = stmtrsMatch.Groups[1].Value;
        }
        else
        {
            // Fallback: look for BANKACCTFROM anywhere before the first STMTTRN
            var firstTrn = content.IndexOf("<STMTTRN>", StringComparison.OrdinalIgnoreCase);
            searchArea = firstTrn > 0 ? content[..firstTrn] : content;
        }

        // Try SGML-style extraction
        var sgmlPattern = new Regex(@"<BANKACCTFROM>(.*?)(?:</BANKACCTFROM>|<BANKTRANLIST>|<STMTTRN>)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var match = sgmlPattern.Match(searchArea);
        if (match.Success)
        {
            var block = match.Groups[1].Value;
            // Try XML-style tag first
            var xmlAcctId = Regex.Match(block, @"<ACCTID>\s*(.*?)\s*</ACCTID>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (xmlAcctId.Success)
                return xmlAcctId.Groups[1].Value.Trim();

            // SGML-style tag
            var sgmlAcctId = Regex.Match(block, @"<ACCTID>\s*([^\r\n<]+)", RegexOptions.IgnoreCase);
            if (sgmlAcctId.Success)
                return sgmlAcctId.Groups[1].Value.Trim();
        }

        return string.Empty;
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

            // Extract opposing account info from BANKACCTTO or BANKACCTFROM blocks
            var (opposingAcctNum, opposingBankId) = ExtractOpposingAccountSgml(body);

            transactions.Add(new FIIITransaction
            {
                FitId                  = ReadSgmlTag(body, "FITID"),
                TransactionType        = ReadSgmlTag(body, "TRNTYPE"),
                Date                   = ParseOFXDate(ReadSgmlTag(body, "DTPOSTED")),
                Amount                 = ParseAmount(ReadSgmlTag(body, "TRNAMT")),
                Name                   = ReadSgmlTag(body, "NAME"),
                Memo                   = ReadSgmlTag(body, "MEMO"),
                OpposingAccountNumber  = opposingAcctNum,
                OpposingBankId         = opposingBankId,
            });
        }

        return transactions;
    }

    private static string ReadSgmlTag(string body, string tag)
    {
        // SGML tags may or may not be closed: <TAG>value or <TAG>value</TAG>
        var pattern = $@"<{tag}>\s*([^\r\n<]+)";
        var match = Regex.Match(body, pattern, RegexOptions.IgnoreCase);
        return match.Success ? DecodeEntities(match.Groups[1].Value.Trim()) : string.Empty;
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

            // Extract opposing account info from BANKACCTTO or BANKACCTFROM blocks
            var (opposingAcctNum, opposingBankId) = ExtractOpposingAccountXml(body);

            transactions.Add(new FIIITransaction
            {
                FitId                  = ReadXmlTag(body, "FITID"),
                TransactionType        = ReadXmlTag(body, "TRNTYPE"),
                Date                   = ParseOFXDate(ReadXmlTag(body, "DTPOSTED")),
                Amount                 = ParseAmount(ReadXmlTag(body, "TRNAMT")),
                Name                   = ReadXmlTag(body, "NAME"),
                Memo                   = ReadXmlTag(body, "MEMO"),
                OpposingAccountNumber  = opposingAcctNum,
                OpposingBankId         = opposingBankId,
            });
        }

        return transactions;
    }

    private static string ReadXmlTag(string body, string tag)
    {
        var pattern = $@"<{tag}>\s*(.*?)\s*</{tag}>";
        var match = Regex.Match(body, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? DecodeEntities(match.Groups[1].Value.Trim()) : string.Empty;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes SGML/XML character entities (&amp;, &lt;, &gt;, &apos;, &quot;)
    /// and numeric character references (&#NNN; / &#xHHH;).
    /// </summary>
    private static string DecodeEntities(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('&'))
            return value;

        return System.Net.WebUtility.HtmlDecode(value);
    }

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

    // ── Opposing account extraction ──────────────────────────────────────────

    // Matches <BANKACCTTO>…</BANKACCTTO> or <BANKACCTFROM>…</BANKACCTFROM> blocks (SGML style)
    private static readonly Regex SgmlBankAcctToPattern = new(
        @"<BANKACCTTO>(.*?)(?:</BANKACCTTO>|<(?!ACCTID|BANKID|ACCTTYPE))",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SgmlBankAcctFromPattern = new(
        @"<BANKACCTFROM>(.*?)(?:</BANKACCTFROM>|<(?!ACCTID|BANKID|ACCTTYPE))",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the opposing account number and bank ID from BANKACCTTO/BANKACCTFROM
    /// in an SGML-style OFX transaction block. Prefers BANKACCTTO (the destination
    /// account for the transfer).
    /// </summary>
    private static (string accountNumber, string bankId) ExtractOpposingAccountSgml(string body)
    {
        // Try BANKACCTTO first (most common for transfers in OFX)
        var match = SgmlBankAcctToPattern.Match(body);
        if (match.Success)
        {
            var block = match.Groups[1].Value;
            var acctId = ReadSgmlTag(block, "ACCTID");
            var bankId = ReadSgmlTag(block, "BANKID");
            if (!string.IsNullOrWhiteSpace(acctId))
                return (acctId, bankId);
        }

        // Fallback to BANKACCTFROM (less common in transaction blocks, but some banks use it)
        match = SgmlBankAcctFromPattern.Match(body);
        if (match.Success)
        {
            var block = match.Groups[1].Value;
            var acctId = ReadSgmlTag(block, "ACCTID");
            var bankId = ReadSgmlTag(block, "BANKID");
            if (!string.IsNullOrWhiteSpace(acctId))
                return (acctId, bankId);
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// Extracts the opposing account number and bank ID from BANKACCTTO/BANKACCTFROM
    /// in an XML-style OFX transaction block.
    /// </summary>
    private static (string accountNumber, string bankId) ExtractOpposingAccountXml(string body)
    {
        // Try BANKACCTTO first
        var toPattern = new Regex(
            @"<BANKACCTTO>(.*?)</BANKACCTTO>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var match = toPattern.Match(body);
        if (match.Success)
        {
            var block = match.Groups[1].Value;
            var acctId = ReadXmlTag(block, "ACCTID");
            var bankId = ReadXmlTag(block, "BANKID");
            if (!string.IsNullOrWhiteSpace(acctId))
                return (acctId, bankId);
        }

        // Fallback to BANKACCTFROM
        var fromPattern = new Regex(
            @"<BANKACCTFROM>(.*?)</BANKACCTFROM>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        match = fromPattern.Match(body);
        if (match.Success)
        {
            var block = match.Groups[1].Value;
            var acctId = ReadXmlTag(block, "ACCTID");
            var bankId = ReadXmlTag(block, "BANKID");
            if (!string.IsNullOrWhiteSpace(acctId))
                return (acctId, bankId);
        }

        return (string.Empty, string.Empty);
    }
}
