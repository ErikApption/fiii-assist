using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiiiAssist.FireflyIII;

public partial class Client
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        // Skip null strings so the API doesn't receive invalid empty fields
        // (e.g. currency_id = null won't be serialized).
        settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        // Use a custom DateTimeOffset converter that omits default (0001-01-01) values,
        // since the generated client uses non-nullable DateTimeOffset for optional date fields.
        settings.Converters.Add(new SkipDefaultDateTimeOffsetConverter());
    }
}

/// <summary>
/// Writes DateTimeOffset as a date string, but writes null when the value is
/// <see cref="DateTimeOffset.MinValue"/> (the default for unset non-nullable fields).
/// This prevents sending "0001-01-01" for optional date fields like end_date.
/// </summary>
internal sealed class SkipDefaultDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDateTimeOffset();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        if (value == DateTimeOffset.MinValue)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
