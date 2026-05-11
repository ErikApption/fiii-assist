namespace QfxWatcher.ViewModels;

public record ImportResult(
    int SuccessCount,
    int FailureCount,
    string? ErrorSummary);
