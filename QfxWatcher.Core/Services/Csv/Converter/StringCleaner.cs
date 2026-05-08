using System.Text;

namespace QfxWatcher.Services.Csv.Converter;

/// <summary>
/// Utility class for cleaning strings, equivalent to Firefly III's "Steam" helper.
/// Removes control characters and optionally newlines.
/// </summary>
public static class StringCleaner
{
    /// <summary>
    /// Removes control characters but preserves newlines and standard whitespace.
    /// </summary>
    public static string CleanString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            // Keep printable characters, tabs, newlines, and carriage returns
            if (!char.IsControl(c) || c == '\t' || c == '\n' || c == '\r')
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Removes control characters including newlines, leaving only printable characters and tabs.
    /// Consecutive newline characters (\r\n) are collapsed into a single space.
    /// </summary>
    public static string CleanStringAndNewlines(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasNewline = false;
        foreach (var c in value)
        {
            if (c == '\r' || c == '\n')
            {
                if (!lastWasNewline)
                {
                    sb.Append(' ');
                    lastWasNewline = true;
                }
                // Skip additional consecutive newline chars
            }
            else if (!char.IsControl(c) || c == '\t')
            {
                sb.Append(c);
                lastWasNewline = false;
            }
            // Other control chars are dropped
        }

        return sb.ToString().Trim();
    }
}
