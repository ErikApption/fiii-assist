namespace QfxWatcher.ViewModels;

public enum WizardStep
{
    Closed,              // Wizard not visible
    AccountSelection,    // Step 1: pick account
    FileSelection,       // Transient: native file picker open
    TransactionPreview,  // Step 2: review transactions
    Importing,           // Step 3: import in progress
    Results              // Step 4: success/failure summary
}
