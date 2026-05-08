using System.Globalization;
using System.Text.RegularExpressions;

namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Converts a date string using a configurable format and locale.
/// Configuration format: "locale:dateFormat" (e.g. "en:Y-m-d") or just "dateFormat".
/// The date format uses PHP-style tokens which are mapped to .NET equivalents.
/// </summary>
public sealed partial class DateConverter : IConverter<DateTime>
{
    private string _dateFormat = "yyyy-MM-dd";
    private CultureInfo _dateLocale = CultureInfo.InvariantCulture;

    public DateTime Convert(string? value)
    {
        var str = StringCleaner.CleanStringAndNewlines(value ?? "").Trim();

        if (string.IsNullOrEmpty(str))
            return DateTime.Today;

        try
        {
            var parsed = DateTime.ParseExact(str, _dateFormat, _dateLocale, DateTimeStyles.None);

            if (parsed.Year < 1984)
                parsed = new DateTime(DateTime.Now.Year, parsed.Month, parsed.Day, 0, 0, 0);

            return parsed;
        }
        catch (FormatException)
        {
            if (DateTime.TryParse(str, _dateLocale, DateTimeStyles.None, out var fallback))
            {
                if (fallback.Year < 1984)
                    fallback = new DateTime(DateTime.Now.Year, fallback.Month, fallback.Day, 0, 0, 0);

                return fallback;
            }

            return DateTime.Today;
        }
    }

    public void SetConfiguration(string configuration)
    {
        var (locale, format) = SplitLocaleFormat(configuration);
        _dateLocale = GetCultureInfo(locale);
        _dateFormat = ConvertPhpDateFormat(format);
    }

    /// <summary>
    /// Splits a configuration string like "en:Y-m-d" into locale and format parts.
    /// </summary>
    public static (string Locale, string Format) SplitLocaleFormat(string format)
    {
        var match = LocaleFormatRegex().Match(format);
        if (match.Success && match.Groups.Count == 3)
        {
            var locale = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : "en";
            var dateFormat = match.Groups[2].Value;
            return (locale, dateFormat);
        }

        return ("en", format);
    }

    /// <summary>
    /// Converts common PHP date format tokens to .NET equivalents.
    /// Uses character-by-character replacement to avoid token collision.
    /// </summary>
    internal static string ConvertPhpDateFormat(string phpFormat)
    {
        // Remove leading '!' used in PHP for resetting time
        if (phpFormat.StartsWith('!'))
            phpFormat = phpFormat[1..];

        // Map PHP date tokens to .NET format tokens (character by character to avoid collisions)
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < phpFormat.Length; i++)
        {
            var c = phpFormat[i];
            var mapped = c switch
            {
                'Y' => "yyyy",  // 4-digit year
                'y' => "yy",    // 2-digit year
                'm' => "MM",    // Month with leading zero
                'n' => "M",     // Month without leading zero
                'd' => "dd",    // Day with leading zero
                'j' => "d",     // Day without leading zero
                'H' => "HH",    // 24-hour with leading zero
                'G' => "H",     // 24-hour without leading zero
                'h' => "hh",    // 12-hour with leading zero
                'g' => "h",     // 12-hour without leading zero
                'i' => "mm",    // Minutes
                's' => "ss",    // Seconds
                'A' => "tt",    // AM/PM
                'a' => "tt",    // am/pm
                'D' => "ddd",   // Short day name
                'l' => "dddd",  // Full day name
                'M' => "MMM",   // Short month name
                'F' => "MMMM",  // Full month name
                _ => c.ToString()
            };
            result.Append(mapped);
        }

        return result.ToString();
    }

    private static CultureInfo GetCultureInfo(string locale)
    {
        try
        {
            return new CultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    [GeneratedRegex(@"^(?:([a-z]{2,5}):)?(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex LocaleFormatRegex();
}
