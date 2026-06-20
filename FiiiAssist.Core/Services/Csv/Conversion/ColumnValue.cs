using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Services.Csv.Conversion;

/// <summary>
/// Represents a single column value from a CSV line, enriched with role and mapping information.
/// </summary>
public sealed class ColumnValue
{
    /// <summary>
    /// The raw string value from the CSV cell.
    /// </summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// The role assigned to this column (may differ from OriginalRole if mapping changed it).
    /// </summary>
    public string Role { get; set; } = "_ignore";

    /// <summary>
    /// The original role assigned to this column before mapping adjustments.
    /// </summary>
    public string OriginalRole { get; set; } = "_ignore";

    /// <summary>
    /// If the value was mapped to a Firefly III entity ID, this holds that ID. 0 means unmapped.
    /// </summary>
    public int MappedValue { get; set; }

    /// <summary>
    /// Whether this column's value should be appended to the existing field value (e.g. multiple description columns).
    /// </summary>
    public bool AppendValue { get; set; }

    /// <summary>
    /// Optional configuration string passed to the converter (e.g. date format).
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Gets the parsed/converted value. If mapped, returns the mapped ID; otherwise runs the converter.
    /// </summary>
    public object? GetParsedValue()
    {
        if (MappedValue != 0)
            return MappedValue;

        var converterClass = RoleDefinitions.GetConverterName(Role);
        if (string.IsNullOrEmpty(converterClass))
            return Value;

        return ConverterService.Convert(converterClass, Value, Configuration);
    }
}
