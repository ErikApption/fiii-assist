# Requirements Document

## Introduction

The QFX Import Wizard provides a user-initiated workflow on the Dashboard page that allows users to manually select and import QFX files into Firefly III. Unlike the existing file-watcher-triggered import dialog, this wizard is a multi-step flow that guides the user through selecting a bank account from the Firefly III API, picking a QFX file, previewing transactions, and confirming the import.

## Glossary

- **Wizard**: A multi-step UI flow on the Dashboard page that guides the user through the QFX import process
- **Dashboard_Page**: The main page of the QfxWatcher application displaying status, import log, and the import wizard entry point
- **Firefly_III_Service**: The backend service adapter that communicates with the Firefly III personal finance API
- **QFX_Parser**: The service that parses QFX/OFX files into transaction objects
- **Account_Selector**: The wizard step that displays asset accounts fetched from the Firefly III API for user selection
- **File_Picker**: The native file dialog that allows the user to browse and select a QFX file from the filesystem
- **Transaction_Preview**: The wizard step that displays parsed transactions before import confirmation
- **Import_Engine**: The component that sends parsed transactions to Firefly III via the API

## Requirements

### Requirement 1: Wizard Entry Point

**User Story:** As a user, I want to launch the QFX import wizard from the Dashboard page, so that I can manually import QFX files without relying on the file watcher.

#### Acceptance Criteria

1. THE Dashboard_Page SHALL display a button labeled "Import QFX" in the Status card area
2. WHILE the Firefly_III_Service has not been configured or has failed its most recent connection test, THE Dashboard_Page SHALL disable the "Import QFX" button and display a tooltip indicating that a valid Firefly III connection is required
3. WHEN the user clicks the "Import QFX" button, THE Wizard SHALL open and display the Account_Selector step

### Requirement 2: Account Selection

**User Story:** As a user, I want to select a bank account from a list fetched from the Firefly III API, so that imported transactions are associated with the correct account.

#### Acceptance Criteria

1. WHEN the Wizard opens, THE Account_Selector SHALL fetch the list of active asset accounts from the Firefly_III_Service within 30 seconds
2. WHILE the account list is loading, THE Account_Selector SHALL display a loading indicator
3. WHEN the account list loads successfully with one or more accounts, THE Account_Selector SHALL display each account name in a selectable list
4. IF the account list fetch fails or does not respond within 30 seconds, THEN THE Account_Selector SHALL display an error message indicating the connection failure and a retry button that re-triggers the fetch
5. IF the account list loads successfully but contains zero accounts, THEN THE Account_Selector SHALL display a message indicating no active asset accounts were found and SHALL disable progression to the next step
6. IF a DefaultAccountId is configured in settings and matches an account in the fetched list, THEN THE Account_Selector SHALL pre-select that account
7. THE Wizard SHALL disable the next-step control until the user has selected exactly one account from the list

### Requirement 3: File Selection

**User Story:** As a user, I want to pick a QFX file from my filesystem, so that I can choose which file to import.

#### Acceptance Criteria

1. WHEN the user proceeds past the Account_Selector step, THE Wizard SHALL open the File_Picker dialog
2. THE File_Picker SHALL filter displayed files to show only files with .qfx and .ofx extensions
3. IF the user cancels the File_Picker, THEN THE Wizard SHALL return to the Account_Selector step with the previously selected account preserved
4. WHEN the user selects a file and the QFX_Parser parses it successfully, THE Wizard SHALL advance to the Transaction_Preview step
5. IF the selected file cannot be read due to a file system error, THEN THE Wizard SHALL display an error message indicating the file is inaccessible and allow the user to select a different file

### Requirement 4: Transaction Preview

**User Story:** As a user, I want to preview the transactions parsed from the QFX file before importing, so that I can verify the data is correct.

#### Acceptance Criteria

1. WHEN a QFX file is parsed successfully, THE Transaction_Preview SHALL display the total number of transactions found
2. WHEN a QFX file is parsed successfully, THE Transaction_Preview SHALL display a scrollable list showing each transaction's date, name, and amount with sign (negative for debits, positive for credits), ordered by transaction date descending (most recent first)
3. WHEN a QFX file is parsed successfully, THE Transaction_Preview SHALL display the selected account name as the import target
4. IF the QFX_Parser finds zero transactions in the file, THEN THE Transaction_Preview SHALL display a warning message indicating no transactions were found and disable the import action
5. IF the QFX_Parser fails to parse the file, THEN THE Wizard SHALL display an error message indicating the file could not be parsed and return the user to the File_Picker to select a different file

### Requirement 5: Import Execution

**User Story:** As a user, I want to confirm and execute the import, so that the transactions are created in Firefly III.

#### Acceptance Criteria

1. WHEN the user confirms the import, THE Import_Engine SHALL send each transaction to the Firefly_III_Service for the selected account
2. WHILE the import is in progress, THE Wizard SHALL display a progress indicator showing the number of transactions processed out of the total count, and disable the confirm button
3. WHEN all transactions are imported without failure, THE Wizard SHALL display a success summary with the count of imported transactions
4. WHEN all transactions are imported without failure, THE Wizard SHALL add an entry to the Dashboard import log recording the file name, account name, transaction count, and timestamp
5. IF one or more transactions fail to import, THEN THE Import_Engine SHALL continue importing remaining transactions and THE Wizard SHALL display a completion summary showing both the count of successfully imported transactions and the count of failed transactions
6. IF one or more transactions fail to import, THEN THE Wizard SHALL add an entry to the Dashboard import log with the file name, account name, successful transaction count, and an error message indicating the failure count
7. WHEN the import completes, THE Wizard SHALL provide an option to close the wizard or to import another file starting from the File_Picker step with the same account selected

### Requirement 6: Wizard Navigation

**User Story:** As a user, I want to navigate back through wizard steps, so that I can correct my selections without restarting.

#### Acceptance Criteria

1. WHILE the Wizard is on the Transaction_Preview step, THE Wizard SHALL display a back button that returns the user to the Account_Selector step
2. WHILE the import is in progress, THE Wizard SHALL disable the back button
3. WHEN the user navigates back from Transaction_Preview to the Account_Selector step, THE Wizard SHALL preserve the previously selected account
4. WHEN the user proceeds forward again from the Account_Selector step after navigating back, THE Wizard SHALL open the File_Picker dialog to allow file re-selection
5. WHILE the Wizard is displaying the import results, THE Wizard SHALL hide the back button
