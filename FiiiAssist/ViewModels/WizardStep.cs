namespace FiiiAssist.ViewModels;

public enum WizardStep
{
    Closed,                    // Wizard not visible
    AccountSelection,          // Step 1: pick account
    FileSelection,             // Transient: native file picker open
    AccountUpdateConfirmation, // Step 1b: ask user to update Firefly III account number
    TransactionPreview,        // Step 2: review transactions
    Importing,                 // Step 3: import in progress
    Results                    // Step 4: success/failure summary
}
