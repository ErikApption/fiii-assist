namespace FiiiAssist.Services.Csv.Converter;

/// <summary>
/// Factory service that resolves and invokes converters by class name.
/// </summary>
public static class ConverterService
{
    private static readonly Dictionary<string, Type> ConverterTypes = BuildConverterMap();

    /// <summary>
    /// Convert a value using the named converter class.
    /// Accepts either the full class name (e.g. "AmountConverter") or the short name (e.g. "Amount").
    /// Returns the typed result from the converter.
    /// </summary>
    public static object? Convert(string className, string? value, string? configuration = null)
    {
        if (string.IsNullOrEmpty(className))
            return value;

        if (!ConverterTypes.TryGetValue(className, out var type))
        {
            // Try with "Converter" suffix for backward compatibility
            if (!ConverterTypes.TryGetValue(className + "Converter", out type))
                throw new InvalidOperationException($"No such converter: \"{className}\"");
        }

        var converter = Activator.CreateInstance(type)!;

        if (configuration is not null)
        {
            var setConfig = type.GetMethod(nameof(IConverter<object>.SetConfiguration));
            setConfig?.Invoke(converter, [configuration]);
        }

        var convertMethod = type.GetMethod(nameof(IConverter<object>.Convert));
        return convertMethod?.Invoke(converter, [value]);
    }

    /// <summary>
    /// Convert a value using a specific converter type with full type safety.
    /// </summary>
    public static TOutput Convert<TOutput>(IConverter<TOutput> converter, string? value, string? configuration = null)
    {
        if (configuration is not null)
            converter.SetConfiguration(configuration);

        return converter.Convert(value);
    }

    /// <summary>
    /// Check if a converter class exists by name.
    /// Accepts either the full class name or the short name.
    /// </summary>
    public static bool Exists(string className)
        => ConverterTypes.ContainsKey(className) || ConverterTypes.ContainsKey(className + "Converter");

    private static Dictionary<string, Type> BuildConverterMap()
    {
        var assembly = typeof(ConverterService).Assembly;
        var converterInterface = typeof(IConverter<>);

        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract)
                continue;

            var implements = type.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == converterInterface);

            if (implements)
                map[type.Name] = type;
        }

        return map;
    }
}
