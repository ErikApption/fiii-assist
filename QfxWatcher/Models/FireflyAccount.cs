namespace QfxWatcher.Models;

/// <summary>
/// An account returned by the Firefly III REST API.
/// </summary>
public class FireflyAccount
{
    public string Id   { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool   Active { get; set; } = true;

    /// <summary>Convenience display name shown in the UI.</summary>
    public string DisplayName => Active ? Name : $"{Name} (inactive)";

    public override string ToString() => DisplayName;
}
