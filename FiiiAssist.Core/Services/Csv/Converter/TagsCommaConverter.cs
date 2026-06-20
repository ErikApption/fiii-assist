namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Splits a value into tags using comma as the delimiter.
/// </summary>
public sealed class TagsCommaConverter : IConverter<string[]>
{
    public string[] Convert(string? value)
    {
        var str = StringCleaner.CleanStringAndNewlines(value ?? "");

        return str.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToArray();
    }

    public void SetConfiguration(string configuration) { }
}
