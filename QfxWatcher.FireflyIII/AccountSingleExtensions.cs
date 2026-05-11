namespace QfxWatcher.FireflyIII;

/// <summary>
/// Convenience properties for data-binding in WinUI controls.
/// </summary>
public partial class AccountSingle
{
    /// <summary>Account ID suitable for SelectedValuePath binding.</summary>
    public string Id => Data?.Id ?? string.Empty;

    /// <summary>Account display name suitable for DisplayMemberPath binding.</summary>
    public string Name => Data?.Attributes?.Name ?? string.Empty;
}
