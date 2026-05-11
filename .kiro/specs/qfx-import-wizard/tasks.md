# Implementation Plan: QFX Import Wizard

## Overview

This plan implements a multi-step inline import wizard on the Dashboard page. The wizard guides users through account selection, file picking, transaction preview, and import execution. Implementation uses C# with WinUI 3, CommunityToolkit.Mvvm source generators, and reuses existing `FireflyIIIService` and `QfxParserService` from `QfxWatcher.Core`.

## Tasks

- [x] 1. Create WizardStep enum and ImportResult record
  - [x] 1.1 Create `WizardStep` enum and `ImportResult` record in `QfxWatcher/ViewModels/`
    - Create `WizardStep.cs` with enum values: Closed, AccountSelection, FileSelection, TransactionPreview, Importing, Results
    - Create `ImportResult.cs` as a record with SuccessCount, FailureCount, and ErrorSummary properties
    - _Requirements: 1.3, 5.3, 5.5_

- [x] 2. Implement ImportWizardViewModel core state machine
  - [x] 2.1 Create `ImportWizardViewModel` with observable properties and step management
    - Create `QfxWatcher/ViewModels/ImportWizardViewModel.cs`
    - Add observable properties: CurrentStep, IsLoading, ErrorMessage, CanGoNext, CanGoBack
    - Add ObservableCollection for Accounts and Transactions
    - Add SelectedAccount, SelectedFilePath, SelectedFileName properties
    - Add import progress properties: ImportedCount, FailedCount, TotalCount
    - Inject `FireflyIIIService` and `SettingsService` via constructor
    - _Requirements: 1.3, 2.1, 2.7, 6.1, 6.2, 6.5_

  - [x] 2.2 Implement `OpenWizardAsync` command with account fetching
    - Transition CurrentStep from Closed to AccountSelection
    - Call `FireflyIIIService.GetAccountsAsync()` to populate Accounts collection
    - Set IsLoading during fetch, handle timeout (30s) and errors
    - Pre-select account matching `DefaultAccountId` from settings
    - Implement `RetryLoadAccountsAsync` command for error recovery
    - _Requirements: 1.3, 2.1, 2.2, 2.3, 2.4, 2.6_

  - [x] 2.3 Implement account selection and navigation commands
    - Implement `SelectAccount` command that sets SelectedAccount and updates CanGoNext
    - Implement `ProceedToFileSelection` command that transitions to FileSelection step
    - Implement `GoBack` command that returns to AccountSelection preserving SelectedAccount
    - Implement `Close` command that resets wizard to Closed state
    - Implement `ImportAnother` command that returns to FileSelection with same account
    - _Requirements: 2.7, 3.3, 5.7, 6.1, 6.3, 6.4_

  - [x] 2.4 Implement file parsing and transaction preview logic
    - Call `QfxParserService.ParseFile(path)` when file is selected
    - Populate Transactions collection ordered by date descending
    - Set TransactionCount from parsed results
    - Handle parse failures with error message and return to file selection
    - Disable import action when zero transactions found
    - _Requirements: 3.4, 3.5, 4.1, 4.2, 4.4, 4.5_

  - [x] 2.5 Implement `ConfirmImportAsync` command with progress tracking
    - Send each transaction to `FireflyIIIService.ImportTransactionsAsync()` for selected account
    - Track ImportedCount, FailedCount, TotalCount during execution
    - Continue importing remaining transactions on individual failures
    - Transition to Results step on completion
    - Add ImportLogEntry to DashboardViewModel.LogEntries on completion
    - Pass `ErrorIfDuplicateHash` setting from AppSettings
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

- [x] 3. Checkpoint - Ensure ViewModel compiles and logic is sound
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Create ImportWizardPanel UserControl (UI)
  - [x] 4.1 Create `ImportWizardPanel.xaml` UserControl with step-based content switching
    - Create `QfxWatcher/Controls/ImportWizardPanel.xaml` and code-behind
    - Use visibility bindings or ContentControl with DataTemplateSelector to swap step content
    - Implement AccountSelection step UI: loading indicator, account list, error/retry, Next button
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.7_

  - [x] 4.2 Implement TransactionPreview step UI
    - Display total transaction count and selected account name
    - Show scrollable list with date, name, and signed amount per transaction
    - Add Import button (disabled when zero transactions) and Back button
    - Show warning message when no transactions found
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 6.1_

  - [x] 4.3 Implement Importing and Results step UI
    - Show progress indicator with processed/total count during import
    - Disable confirm and back buttons during import
    - Display success summary or partial failure summary on Results step
    - Add Close and "Import Another" buttons on Results step
    - Hide back button on Results step
    - _Requirements: 5.2, 5.3, 5.5, 5.7, 6.2, 6.5_

- [x] 5. Integrate wizard into DashboardPage
  - [x] 5.1 Add "Import QFX" button and wire up DashboardPage
    - Add "Import QFX" button to the Status card in `DashboardPage.xaml`
    - Bind button IsEnabled to `DashboardViewModel.IsConnected`
    - Add tooltip when disabled indicating valid Firefly III connection required
    - Embed `ImportWizardPanel` below the status card and above the import log
    - _Requirements: 1.1, 1.2_

  - [x] 5.2 Wire ImportWizardViewModel into DashboardViewModel
    - Add `ImportWizardViewModel` as a property on `DashboardViewModel`
    - Register ViewModel in `App.xaml.cs` DI container (or instantiate in DashboardViewModel constructor)
    - Connect "Import QFX" button click to `ImportWizardViewModel.OpenWizardAsync`
    - Wire import completion to add entries to `DashboardViewModel.LogEntries`
    - _Requirements: 1.3, 5.4, 5.6_

  - [x] 5.3 Implement FilePicker integration in code-behind
    - Open native `FileOpenPicker` filtered to .qfx and .ofx extensions when FileSelection step is reached
    - Pass selected file path to `ImportWizardViewModel.FileSelected(path)`
    - Handle picker cancellation by calling GoBack on the ViewModel
    - _Requirements: 3.1, 3.2, 3.3_

- [x] 6. Checkpoint - Ensure full UI integration compiles and wizard flow works end-to-end
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Set up test project and write unit tests
  - [x] 7.1 Create `QfxWatcher.Tests` project with xUnit and FsCheck dependencies
    - Create `QfxWatcher.Tests/QfxWatcher.Tests.csproj` targeting net10.0
    - Add package references: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FsCheck, FsCheck.Xunit, Moq (or NSubstitute)
    - Add project references to QfxWatcher and QfxWatcher.Core
    - Create `QfxWatcher.Tests/ViewModels/` directory structure
    - _Requirements: All_

  - [x] 7.2 Write unit tests for ImportWizardViewModel
    - Create `QfxWatcher.Tests/ViewModels/ImportWizardViewModelTests.cs`
    - Test: Button disabled when not connected (Req 1.2)
    - Test: Loading indicator shown during account fetch (Req 2.2)
    - Test: Empty account list disables Next (Req 2.5)
    - Test: Successful parse advances to preview (Req 3.4)
    - Test: Zero transactions disables import (Req 4.4)
    - Test: Parse failure returns to file selection (Req 4.5)
    - Test: Successful import adds log entry (Req 5.4)
    - Test: Partial failure adds log entry with error (Req 5.6)
    - Test: Results step shows Close and ImportAnother (Req 5.7)
    - Test: Forward after back opens file picker (Req 6.4)
    - _Requirements: 1.2, 2.2, 2.5, 3.4, 4.4, 4.5, 5.4, 5.6, 5.7, 6.4_

- [x] 8. Write property-based tests for ImportWizardViewModel
  - [x] 8.1 Write property test: Wizard cannot open without valid connection
    - **Property 1: Wizard cannot open without a valid connection**
    - **Validates: Requirements 1.2**

  - [x] 8.2 Write property test: Opening wizard transitions to AccountSelection
    - **Property 2: Opening the wizard transitions to AccountSelection**
    - **Validates: Requirements 1.3**

  - [x] 8.3 Write property test: All fetched accounts exposed in collection
    - **Property 3: All fetched accounts are exposed in the collection**
    - **Validates: Requirements 2.3**

  - [x] 8.4 Write property test: DefaultAccountId pre-selects matching account
    - **Property 4: DefaultAccountId pre-selects the matching account**
    - **Validates: Requirements 2.6**

  - [x] 8.5 Write property test: CanGoNext requires exactly one selected account
    - **Property 5: CanGoNext requires exactly one selected account**
    - **Validates: Requirements 2.7**

  - [x] 8.6 Write property test: Backward navigation preserves selected account
    - **Property 6: Backward navigation preserves the selected account**
    - **Validates: Requirements 3.3, 6.3**

  - [x] 8.7 Write property test: TransactionCount equals parsed collection size
    - **Property 7: TransactionCount equals the parsed collection size**
    - **Validates: Requirements 4.1**

  - [x] 8.8 Write property test: Transactions ordered by date descending
    - **Property 8: Transactions are ordered by date descending**
    - **Validates: Requirements 4.2**

  - [x] 8.9 Write property test: Navigation controls reflect step constraints
    - **Property 9: Navigation controls reflect step constraints**
    - **Validates: Requirements 5.2, 6.1, 6.2, 6.5**

  - [x] 8.10 Write property test: Import accounting invariant
    - **Property 10: Import accounting invariant (ImportedCount + FailedCount = TotalCount)**
    - **Validates: Requirements 5.5**

- [x] 9. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document using FsCheck
- Unit tests validate specific examples and edge cases
- The wizard reuses existing `FireflyIIIService` and `QfxParserService` — no new service classes needed
- CommunityToolkit.Mvvm source generators handle INotifyPropertyChanged and RelayCommand boilerplate

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "7.1"] },
    { "id": 1, "tasks": ["2.1"] },
    { "id": 2, "tasks": ["2.2", "2.3", "2.4"] },
    { "id": 3, "tasks": ["2.5"] },
    { "id": 4, "tasks": ["4.1", "4.2", "4.3", "5.2"] },
    { "id": 5, "tasks": ["5.1", "5.3"] },
    { "id": 6, "tasks": ["7.2"] },
    { "id": 7, "tasks": ["8.1", "8.2", "8.3", "8.4", "8.5", "8.6", "8.7", "8.8", "8.9", "8.10"] }
  ]
}
```
