using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FiiiAssist.Services.Csv.Conversion;

/// <summary>
/// Reads and sanitizes CSV content into arrays of string values.
/// Handles header skipping, delimiter parsing, and duplicate line removal.
/// </summary>
public sealed class CsvFileProcessor
{
    private readonly CsvImportConfiguration _config;

    public CsvFileProcessor(CsvImportConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Process raw CSV content into sanitized lines (arrays of cell values).
    /// </summary>
    public List<string[]> ProcessCsvContent(string csvContent)
    {
        var lines = ParseCsv(csvContent, _config.Delimiter);

        // Skip header row if configured
        if (_config.HasHeaders && lines.Count > 0)
            lines.RemoveAt(0);

        // Sanitize each line
        for (var i = 0; i < lines.Count; i++)
            lines[i] = Sanitize(lines[i]);

        // Remove duplicate lines if configured
        if (_config.IgnoreDuplicateLines)
            lines = RemoveDuplicateLines(lines);

        return lines;
    }

    /// <summary>
    /// Simple CSV parser that handles quoted fields.
    /// </summary>
    private static List<string[]> ParseCsv(string content, char delimiter)
    {
        var lines = new List<string[]>();
        using var reader = new StringReader(content);

        string? rawLine;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            lines.Add(ParseCsvLine(rawLine, delimiter));
        }

        return lines;
    }

    private static string[] ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Check for escaped quote
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip next quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return [.. fields];
    }

    private static string[] Sanitize(string[] line)
    {
        for (var i = 0; i < line.Length; i++)
            line[i] = line[i].Replace("&nbsp;", " ").Trim();
        return line;
    }

    private static List<string[]> RemoveDuplicateLines(List<string[]> lines)
    {
        var hashes = new HashSet<string>();
        var result = new List<string[]>();

        foreach (var line in lines)
        {
            var json = JsonSerializer.Serialize(line);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

            if (hashes.Add(hash))
                result.Add(line);
        }

        return result;
    }
}
