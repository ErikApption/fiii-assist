namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Cleans an account number by removing spaces and control characters.
/// </summary>
public sealed class AccountNumberConverter : IConverter<string>
{
    public string Convert(string? value)
    {
        var str = StringCleaner.CleanStringAndNewlines(value ?? "");
        return str.Replace(" ", "");
    }

    public void SetConfiguration(string configuration) { }
}
