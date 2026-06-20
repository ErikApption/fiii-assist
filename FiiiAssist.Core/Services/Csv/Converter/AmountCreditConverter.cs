namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Converts an amount and always returns a positive decimal value.
/// Returns 0 for null/empty input.
/// </summary>
public sealed class AmountCreditConverter : IConverter<decimal>
{
    public decimal Convert(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        var result = new AmountConverter().Convert(value);
        return AmountConverter.Positive(result);
    }

    public void SetConfiguration(string configuration) { }
}
