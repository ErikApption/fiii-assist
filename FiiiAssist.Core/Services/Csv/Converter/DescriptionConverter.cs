namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Trims a description value.
/// </summary>
public sealed class DescriptionConverter : IConverter<string>
{
    public string Convert(string? value)
    {
        return (value ?? "").Trim();
    }

    public void SetConfiguration(string configuration) { }
}
