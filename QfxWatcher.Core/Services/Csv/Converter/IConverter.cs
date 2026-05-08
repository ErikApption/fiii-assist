namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Base interface for CSV field converters with typed output.
/// </summary>
public interface IConverter<out TOutput>
{
    /// <summary>
    /// Convert a raw CSV string value into a typed result.
    /// </summary>
    TOutput Convert(string? value);

    /// <summary>
    /// Add extra configuration parameters (e.g. date format).
    /// </summary>
    void SetConfiguration(string configuration);
}
