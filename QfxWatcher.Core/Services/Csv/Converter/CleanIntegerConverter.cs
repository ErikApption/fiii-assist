namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Converts a value to an integer. Returns 0 for non-numeric input.
/// </summary>
public sealed class CleanIntegerConverter : IConverter<int>
{
    public int Convert(string? value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    public void SetConfiguration(string configuration) { }
}
