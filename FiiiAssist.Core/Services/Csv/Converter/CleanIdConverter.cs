namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Converts a value to an integer ID, returning null if the value is zero or non-numeric.
/// </summary>
public sealed class CleanIdConverter : IConverter<int?>
{
    public int? Convert(string? value)
    {
        if (!int.TryParse(value, out var id))
            return null;

        return id == 0 ? null : id;
    }

    public void SetConfiguration(string configuration) { }
}
