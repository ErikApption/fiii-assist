using System.Text.Json;

namespace FiiiAssist.Cli;

/// <summary>
/// Tracks import progress per-account to enable resuming interrupted imports.
/// Saved as a JSON file alongside the SQLite database.
/// </summary>
public sealed class ImportProgress
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Per-account progress entry.
    /// </summary>
    public class AccountProgress
    {
        public string ActualAccountId { get; set; } = string.Empty;
        public string ActualAccountName { get; set; } = string.Empty;
        public string FireflyAccountId { get; set; } = string.Empty;
        public int TotalTransactions { get; set; }
        public int ImportedCount { get; set; }
        public int ErrorCount { get; set; }
        public bool Completed { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public Dictionary<string, AccountProgress> Accounts { get; set; } = new();
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }

    private string? _filePath;

    /// <summary>
    /// Loads progress from the JSON file, or creates a new instance if none exists.
    /// </summary>
    public static ImportProgress Load(string progressFilePath)
    {
        ImportProgress progress;

        if (File.Exists(progressFilePath))
        {
            try
            {
                var json = File.ReadAllText(progressFilePath);
                progress = JsonSerializer.Deserialize<ImportProgress>(json, JsonOptions) ?? new();
            }
            catch
            {
                progress = new ImportProgress();
            }
        }
        else
        {
            progress = new ImportProgress();
        }

        progress._filePath = progressFilePath;
        return progress;
    }

    /// <summary>
    /// Returns true if the given account has already been fully imported.
    /// </summary>
    public bool IsAccountCompleted(string actualAccountId)
    {
        return Accounts.TryGetValue(actualAccountId, out var entry) && entry.Completed;
    }

    /// <summary>
    /// Marks an account as completed and saves progress.
    /// </summary>
    public void MarkCompleted(string actualAccountId, string actualAccountName, string fireflyAccountId, int total, int imported, int errors)
    {
        Accounts[actualAccountId] = new AccountProgress
        {
            ActualAccountId = actualAccountId,
            ActualAccountName = actualAccountName,
            FireflyAccountId = fireflyAccountId,
            TotalTransactions = total,
            ImportedCount = imported,
            ErrorCount = errors,
            Completed = true,
            CompletedAt = DateTime.UtcNow,
        };

        Save();
    }

    /// <summary>
    /// Saves the current progress to disk.
    /// </summary>
    public void Save()
    {
        if (_filePath is null) return;

        LastUpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Gets the default progress file path (same directory as the DB, named import-progress.json).
    /// </summary>
    public static string GetDefaultPath(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath) ?? ".";
        return Path.Combine(dir, "import-progress.json");
    }
}
