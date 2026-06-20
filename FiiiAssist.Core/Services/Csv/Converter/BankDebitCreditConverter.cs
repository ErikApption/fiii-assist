namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Converts a bank debit/credit indicator to a multiplier (-1 or 1).
/// Recognizes various international bank indicators for debit transactions.
/// </summary>
public sealed class BankDebitCreditConverter : IConverter<int>
{
    private static readonly HashSet<string> NegativeIndicators = new(StringComparer.OrdinalIgnoreCase)
    {
        "d",       // Old style Rabobank (NL). Short for "Debit"
        "a",       // New style Rabobank (NL). Short for "Af"
        "dr",      // https://old.reddit.com/r/FireflyIII/comments/bn2edf/
        "af",      // ING (NL)
        "db",      // Bank BCA (ID)
        "debet",   // Triodos (NL)
        "debit",   // ING (EN)
        "s",       // Volksbank (DE), Short for "Soll"
        "dbit",    // Banking4 App
        "charge",  // Unknown bank
        "(-)",     // Banco Bolivariano (Ecuador)
        "out",     // Wise
    };

    public int Convert(string? value)
    {
        var str = value?.Trim() ?? "";
        return NegativeIndicators.Contains(str) ? -1 : 1;
    }

    public void SetConfiguration(string configuration) { }
}
