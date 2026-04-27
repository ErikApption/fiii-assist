namespace QfxWatcher.Models;

/// <summary>
/// An account returned by the Actual Budget REST API.
/// </summary>
public class ActualAccount
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Closed { get; set; }
    public bool OffBudget { get; set; }

    /// <summary>Convenience display name shown in the UI.</summary>
    public string DisplayName => Closed ? $"{Name} (closed)" : Name;

    public override string ToString() => DisplayName;
}
