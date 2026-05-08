namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Cleans and validates a URL value. Returns null if the URL is invalid.
/// </summary>
public sealed class CleanUrlConverter : IConverter<string?>
{
    public string? Convert(string? value)
    {
        var str = StringCleaner.CleanStringAndNewlines(value ?? "");
        str = str.Replace("\n", "").Replace("\r", "").Trim();

        if (Uri.TryCreate(str, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return str;
        }

        return null;
    }

    public void SetConfiguration(string configuration) { }
}
