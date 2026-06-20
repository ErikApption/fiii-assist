using System.Numerics;

namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Validates and normalizes an IBAN value.
/// Returns an empty string if the IBAN is invalid.
/// </summary>
public sealed class IbanConverter : IConverter<string>
{
    public string Convert(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        if (IsValidIban(value))
        {
            var cleaned = StringCleaner.CleanStringAndNewlines(value);
            return cleaned.Replace(" ", "").ToUpperInvariant().Trim();
        }

        return "";
    }

    /// <summary>
    /// Validates an IBAN using the MOD-97 algorithm.
    /// </summary>
    public static bool IsValidIban(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var cleaned = StringCleaner.CleanStringAndNewlines(value)
            .Replace(" ", "")
            .ToUpperInvariant()
            .Trim();

        if (cleaned.Length < 4)
            return false;

        // Move first 4 characters to end
        var rearranged = cleaned[4..] + cleaned[..4];

        // Replace letters with numbers (A=10, B=11, ..., Z=35)
        var numericString = string.Concat(rearranged.Select(c =>
            char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString()));

        // Perform MOD 97 check
        if (!BigInteger.TryParse(numericString, out var number))
            return false;

        return number % 97 == 1;
    }

    public void SetConfiguration(string configuration) { }
}
