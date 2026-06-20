namespace FiiiAssist.Models;

/// <summary>
/// Maps a Firefly III account to a filename regex pattern.
/// When a QFX file matches the regex, it is automatically associated with this account.
/// </summary>
public class BankAccountMapping
{
    /// <summary>Firefly III account ID.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Display name of the account (cached from Firefly III).</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Regex pattern matched against the QFX filename.
    /// E.g. "(?i)chequing" would match any file containing "chequing" in its name.
    /// </summary>
    public string FileNamePattern { get; set; } = string.Empty;
}
