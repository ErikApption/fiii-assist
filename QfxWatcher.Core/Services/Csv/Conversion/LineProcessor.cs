namespace QfxWatcher.Services.Csv.Conversion;

/// <summary>
/// Processes a single CSV line into an array of ColumnValue objects,
/// each holding the value, role, mapped value, and configuration.
/// </summary>
public sealed class LineProcessor
{
    private readonly CsvImportConfiguration _config;

    public LineProcessor(CsvImportConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Process multiple CSV lines into arrays of ColumnValues.
    /// </summary>
    public List<List<ColumnValue>> ProcessLines(IEnumerable<string[]> lines)
    {
        var result = new List<List<ColumnValue>>();
        foreach (var line in lines)
            result.Add(ProcessLine(line));
        return result;
    }

    /// <summary>
    /// Process a single CSV line into ColumnValue objects.
    /// </summary>
    public List<ColumnValue> ProcessLine(string[] line)
    {
        var result = new List<ColumnValue>();

        for (var i = 0; i < line.Length; i++)
        {
            var value = line[i].Trim();
            var originalRole = _config.Roles.GetValueOrDefault(i, "_ignore");

            if (originalRole == "_ignore")
                continue;

            if (string.IsNullOrEmpty(value))
                continue;

            // Check if a mapped value is present
            var mapped = 0;
            if (_config.Mapping.TryGetValue(i, out var columnMapping))
                columnMapping.TryGetValue(value, out mapped);

            // The role might change because of mapping
            var doMapping = _config.DoMapping.Contains(i);
            var role = RoleDefinitions.GetMappedRole(originalRole, mapped, doMapping);
            var appendValue = RoleDefinitions.IsAppendValue(originalRole);

            var columnValue = new ColumnValue
            {
                Value = value,
                Role = role,
                OriginalRole = originalRole,
                MappedValue = mapped,
                AppendValue = appendValue,
            };

            // If this is a date column, set the date format configuration
            if (RoleDefinitions.IsDateRole(originalRole))
                columnValue.Configuration = _config.DateFormat;

            result.Add(columnValue);
        }

        // Add original source marker
        result.Add(new ColumnValue
        {
            Value = "qfx-watcher-csv-import",
            Role = "original-source",
            OriginalRole = "original-source",
            MappedValue = 0,
            AppendValue = false,
        });

        return result;
    }
}
