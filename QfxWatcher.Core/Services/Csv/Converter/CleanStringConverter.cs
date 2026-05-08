namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Cleans a string value by removing control characters and newlines.
/// </summary>
public sealed class CleanStringConverter : IConverter<string>
{
    public string Convert(string? value)
    {
        var str = StringCleaner.CleanStringAndNewlines(value ?? "");
        return str.Replace("\n", "").Replace("\r", "").Trim();
    }

    public void SetConfiguration(string configuration) { }
}
