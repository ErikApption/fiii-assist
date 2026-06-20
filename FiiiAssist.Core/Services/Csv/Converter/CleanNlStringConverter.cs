namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Cleans a string value, preserving newlines but removing control characters.
/// </summary>
public sealed class CleanNlStringConverter : IConverter<string>
{
    public string Convert(string? value)
    {
        return StringCleaner.CleanString(value ?? "");
    }

    public void SetConfiguration(string configuration) { }
}
