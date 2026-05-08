namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Converts an amount, makes it positive, then negates it (always returns negative).
/// Returns 0 for null/empty input.
/// </summary>
public sealed class AmountDebitConverter : IConverter<decimal>
{
    public decimal Convert(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        var result = new AmountConverter().Convert(value);
        return -AmountConverter.Positive(result);
    }

    public void SetConfiguration(string configuration) { }
}
