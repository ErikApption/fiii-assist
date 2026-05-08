using System.Globalization;
using System.Text.RegularExpressions;

namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Converts a raw amount string into a decimal value.
/// Handles various international formats, thousand separators, and edge cases.
/// </summary>
public sealed partial class AmountConverter : IConverter<decimal>
{
    /// <summary>
    /// Fallback locale used when the decimal separator cannot be determined.
    /// Set to null to disable locale-based fallback.
    /// </summary>
    public static CultureInfo? FallbackLocale { get; set; }

    public static decimal Negative(decimal amount)
        => amount > 0m ? -amount : amount;

    public static decimal Positive(decimal amount)
        => amount < 0m ? -amount : amount;

    public decimal Convert(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        var str = StripAmount(value);
        char? decimalChar = null;

        if (DecimalIsDot(str))
            decimalChar = '.';

        if (DecimalIsComma(str))
            decimalChar = ',';

        // Check for alternative decimal sign (e.g. ".5" or ",1")
        if (decimalChar is null && AlternativeDecimalSign(str))
            decimalChar = GetAlternativeDecimalSign(str);

        // Strip trailing dash (some banks use trailing minus)
        if (str.EndsWith('-'))
            str = str[..^1];

        // Strip trailing dot or comma
        if (str.EndsWith('.') || str.EndsWith(','))
            str = str[..^1];

        // Try to determine decimal from position analysis
        if (decimalChar is null)
        {
            var dotIndex = str.LastIndexOf('.');
            var commaIndex = str.LastIndexOf(',');

            var index = dotIndex >= 0 ? dotIndex : commaIndex;
            if (index >= 0)
            {
                var len = str.Length;
                var pos = len - index;
                if (pos == 4) // exactly 3 digits after separator = thousands separator
                {
                    if (str[index] == ',')
                        decimalChar = '.';
                    else if (str[index] == '.')
                        decimalChar = ',';
                }
            }

            // Use fallback locale if still undetermined
            if (decimalChar is null && FallbackLocale is not null)
            {
                decimalChar = FallbackLocale.NumberFormat.NumberDecimalSeparator[0];
            }
        }

        // Last resort: search from the left for a dot
        if (decimalChar is null)
        {
            var lastDot = str.LastIndexOf('.');
            if (lastDot >= 0)
                decimalChar = '.';
        }

        // Replace separators based on determined decimal character
        if (decimalChar is not null)
        {
            str = ReplaceDecimal(decimalChar.Value, str);
        }
        else
        {
            // No decimal found, strip all separators
            str = str.Replace(".", "").Replace(",", "").Replace(" ", "");
        }

        if (str.StartsWith('.'))
            str = "0" + str;

        if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;

        return 0m;
    }

    public void SetConfiguration(string configuration) { }

    private static string StripAmount(string value)
    {
        // Strip leading double-dash
        if (value.StartsWith("--"))
            value = value[2..];

        // Remove currency symbols and codes
        value = value.Replace("€", "").Replace("EUR", "").Trim();

        // Keep only digits, minus, parentheses, dots, commas, and spaces
        value = AllowedCharsRegex().Replace(value, "");

        // Handle parenthesized negatives: (123.45) -> -123.45
        if (value.StartsWith('(') && value.EndsWith(')'))
            value = "-" + value[1..^1];

        return value.Trim();
    }

    private static bool DecimalIsDot(string value)
    {
        if (value.Length <= 2)
            return false;

        var decimalPosition = value.Length - 3;
        return value[decimalPosition] == '.' || value.LastIndexOf('.') > decimalPosition;
    }

    private static bool DecimalIsComma(string value)
    {
        if (value.Length <= 2)
            return false;

        var decimalPosition = value.Length - 3;
        if (value[decimalPosition] == ',')
            return true;

        // Check for format like "0,xxxxxxxxxx"
        if (value.Count(c => c == ',') == 1 && value.StartsWith("0,"))
            return true;

        return false;
    }

    private static bool AlternativeDecimalSign(string value)
    {
        if (value.Length <= 1)
            return false;

        var altPosition = value.Length - 2;
        return value[altPosition] == '.' || value[altPosition] == ',';
    }

    private static char GetAlternativeDecimalSign(string value)
    {
        var altPosition = value.Length - 2;
        return value[altPosition];
    }

    private static string ReplaceDecimal(char decimalChar, string value)
    {
        if (decimalChar == '.')
        {
            // Decimal is dot, remove commas and spaces (thousand separators)
            value = value.Replace(",", "").Replace(" ", "");
        }
        else
        {
            // Decimal is comma, remove dots and spaces (thousand separators), then replace comma with dot
            value = value.Replace(".", "").Replace(" ", "");
            value = value.Replace(',', '.');
        }

        return value;
    }

    [GeneratedRegex(@"[^\-\(\)\.,0-9 ]")]
    private static partial Regex AllowedCharsRegex();
}
