namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Converts an amount and negates it (flips the sign).
/// </summary>
public sealed class AmountNegatedConverter : IConverter<decimal>
{
    public decimal Convert(string? value)
    {
        var result = new AmountConverter().Convert(value);
        return -result;
    }

    public void SetConfiguration(string configuration) { }
}
