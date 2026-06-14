namespace QfxWatcher.Models;

/// <summary>
/// Result of processing a single transaction during an import operation.
/// </summary>
public enum ImportTransactionResult
{
    /// <summary>Transaction was successfully imported.</summary>
    Imported,

    /// <summary>Transaction was skipped (duplicate or filtered).</summary>
    Skipped,

    /// <summary>Transaction failed to import (API error).</summary>
    Failed
}
