using NSubstitute;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.Services;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="ImportWizardViewModel"/>.
/// Uses NSubstitute to mock the generated Firefly III <see cref="Client"/>
/// and a real <see cref="SettingsService"/> (which falls back to in-memory storage in tests).
/// </summary>
public class ImportWizardViewModelTests : IDisposable
{
    private readonly Client _mockClient;
    private readonly FireflyIIIService _fireflyService;
    private readonly SettingsService _settingsService;
    private readonly ImportWizardViewModel _vm;

    public ImportWizardViewModelTests()
    {
        _mockClient = Substitute.For<Client>(new HttpClient());
        _fireflyService = new FireflyIIIService(_mockClient);
        _settingsService = new SettingsService();
        _vm = new ImportWizardViewModel(_fireflyService, _settingsService);
    }

    public void Dispose()
    {
        _fireflyService.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AccountRead CreateAccount(string id, string name) => new()
    {
        Id = id,
        Type = "accounts",
        Attributes = new AccountProperties { Name = name }
    };

    private static AccountArray CreateAccountArray(params AccountRead[] accounts)
    {
        var array = new AccountArray();
        foreach (var a in accounts)
            array.Data.Add(a);
        return array;
    }

    private void SetupAccountsReturn(params AccountRead[] accounts)
    {
        _mockClient.ListAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<AccountTypeFilter?>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateAccountArray(accounts));
    }



    private string CreateTempQfxFile(int transactionCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.qfx");
        var transactions = string.Empty;
        for (int i = 0; i < transactionCount; i++)
        {
            var date = DateOnly.FromDateTime(DateTime.Today.AddDays(-i));
            transactions += $@"
<STMTTRN>
<TRNTYPE>DEBIT
<DTPOSTED>{date:yyyyMMdd}120000
<TRNAMT>-{10.00m + i}
<FITID>TX{i:D4}
<NAME>Test Payee {i}
</STMTTRN>";
        }

        var content = $@"OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE

<OFX>
<SIGNONMSGSRSV1>
<SONRS>
<STATUS>
<CODE>0
<SEVERITY>INFO
</STATUS>
<DTSERVER>20240101120000
<LANGUAGE>ENG
</SONRS>
</SIGNONMSGSRSV1>
<BANKMSGSRSV1>
<STMTTRNRS>
<TRNUID>0
<STATUS>
<CODE>0
<SEVERITY>INFO
</STATUS>
<STMTRS>
<CURDEF>USD
<BANKACCTFROM>
<BANKID>123456789
<ACCTID>987654321
<ACCTTYPE>CHECKING
</BANKACCTFROM>
<BANKTRANLIST>
<DTSTART>20240101120000
<DTEND>20240131120000
{transactions}
</BANKTRANLIST>
</STMTRS>
</STMTTRNRS>
</BANKMSGSRSV1>
</OFX>";

        File.WriteAllText(path, content);
        return path;
    }

    private string CreateEmptyQfxFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_empty_{Guid.NewGuid()}.qfx");
        var content = @"OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE

<OFX>
<SIGNONMSGSRSV1>
<SONRS>
<STATUS>
<CODE>0
<SEVERITY>INFO
</STATUS>
<DTSERVER>20240101120000
<LANGUAGE>ENG
</SONRS>
</SIGNONMSGSRSV1>
<BANKMSGSRSV1>
<STMTTRNRS>
<TRNUID>0
<STATUS>
<CODE>0
<SEVERITY>INFO
</STATUS>
<STMTRS>
<CURDEF>USD
<BANKACCTFROM>
<BANKID>123456789
<ACCTID>987654321
<ACCTTYPE>CHECKING
</BANKACCTFROM>
<BANKTRANLIST>
<DTSTART>20240101120000
<DTEND>20240131120000
</BANKTRANLIST>
</STMTRS>
</STMTTRNRS>
</BANKMSGSRSV1>
</OFX>";

        File.WriteAllText(path, content);
        return path;
    }

    private string CreateNonExistentFilePath()
    {
        // Return a path that does not exist — File.ReadAllText will throw IOException
        return Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.qfx");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Req 1.2: The Import QFX button should be disabled when the service is not connected.
    /// The wizard starts in Closed state and CanGoNext is false by default.
    /// </summary>
    [Fact]
    public void ButtonDisabledWhenNotConnected_WizardStartsClosedWithCanGoNextFalse()
    {
        // The ViewModel starts in Closed state — the button binding relies on
        // DashboardViewModel.IsConnected, but the wizard itself should not allow
        // progression when in its initial state.
        Assert.Equal(WizardStep.Closed, _vm.CurrentStep);
        Assert.False(_vm.CanGoNext);
    }

    /// <summary>
    /// Req 2.2: A loading indicator should be shown while accounts are being fetched.
    /// </summary>
    [Fact]
    public async Task LoadingIndicatorShownDuringAccountFetch()
    {
        // Arrange: make the account fetch hang until we release it
        var tcs = new TaskCompletionSource<AccountArray>();
        _mockClient.ListAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<AccountTypeFilter?>())
            .Returns(tcs.Task);
        _mockClient.ListAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<AccountTypeFilter?>(),
            Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        // Act: start opening the wizard (will begin loading)
        var openTask = _vm.OpenWizardCommand.ExecuteAsync(null);

        // Allow the async method to start executing
        await Task.Yield();

        // Assert: IsLoading should be true while awaiting
        Assert.True(_vm.IsLoading);

        // Complete the fetch
        tcs.SetResult(CreateAccountArray(CreateAccount("1", "Checking")));
        await openTask;

        // Assert: IsLoading should be false after completion
        Assert.False(_vm.IsLoading);
    }

    /// <summary>
    /// Req 2.5: When the account list is empty, the Next button should be disabled.
    /// </summary>
    [Fact]
    public async Task EmptyAccountListDisablesNext()
    {
        // Arrange: return empty account list
        SetupAccountsReturn();

        // Act
        await _vm.OpenWizardCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(_vm.Accounts);
        Assert.False(_vm.CanGoNext);
    }

    /// <summary>
    /// Req 3.4: When a file is parsed successfully, the wizard advances to TransactionPreview.
    /// </summary>
    [Fact]
    public void SuccessfulParseAdvancesToPreview()
    {
        // Arrange: create a temp QFX file with transactions
        var filePath = CreateTempQfxFile(3);
        try
        {
            // Act
            _vm.FileSelected(filePath);

            // Assert
            Assert.Equal(WizardStep.TransactionPreview, _vm.CurrentStep);
            Assert.Equal(3, _vm.TransactionCount);
            Assert.Equal(3, _vm.Transactions.Count);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 4.4: When zero transactions are found, the import action should be disabled.
    /// </summary>
    [Fact]
    public void ZeroTransactionsDisablesImport()
    {
        // Arrange: create a QFX file with no transactions
        var filePath = CreateEmptyQfxFile();
        try
        {
            // Act
            _vm.FileSelected(filePath);

            // Assert: advances to preview but CanGoNext is false
            Assert.Equal(WizardStep.TransactionPreview, _vm.CurrentStep);
            Assert.Equal(0, _vm.TransactionCount);
            Assert.False(_vm.CanGoNext);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 4.5: When the parser fails, the wizard returns to file selection with an error.
    /// </summary>
    [Fact]
    public void ParseFailureReturnsToFileSelection()
    {
        // Arrange: use a file path that doesn't exist — triggers IOException in ParseFile
        var filePath = CreateNonExistentFilePath();

        // Act
        _vm.FileSelected(filePath);

        // Assert
        Assert.Equal(WizardStep.FileSelection, _vm.CurrentStep);
        Assert.False(string.IsNullOrEmpty(_vm.ErrorMessage));
        Assert.Contains("Could not parse file", _vm.ErrorMessage);
        Assert.False(_vm.CanGoNext);
    }

    /// <summary>
    /// Req 5.4: A successful import adds a log entry via the ImportCompleted event.
    /// </summary>
    [Fact]
    public async Task SuccessfulImportAddsLogEntry()
    {
        // Arrange
        var account = CreateAccount("acc-1", "My Checking");
        SetupAccountsReturn(account);
        await _vm.OpenWizardCommand.ExecuteAsync(null);
        _vm.SelectAccountCommand.Execute(account);

        var filePath = CreateTempQfxFile(2);
        try
        {
            _vm.FileSelected(filePath);

            // Mock successful import calls
            _mockClient.StoreTransactionAsync(
                Arg.Any<Guid?>(),
                Arg.Any<TransactionStore>(),
                Arg.Any<CancellationToken>())
                .Returns(new TransactionSingle());

            ImportLogEntry? logEntry = null;
            _vm.ImportCompleted += (_, entry) => logEntry = entry;

            // Act
            await _vm.ConfirmImportCommand.ExecuteAsync(null);

            // Assert
            Assert.NotNull(logEntry);
            Assert.True(logEntry.Success);
            Assert.Equal(2, logEntry.TransactionCount);
            Assert.Equal("My Checking", logEntry.AccountName);
            Assert.Equal(Path.GetFileName(filePath), logEntry.FileName);
            Assert.Equal(string.Empty, logEntry.ErrorMessage);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 5.6: A partial failure adds a log entry with an error message.
    /// </summary>
    [Fact]
    public async Task PartialFailureAddsLogEntryWithError()
    {
        // Arrange
        var account = CreateAccount("acc-1", "My Checking");
        SetupAccountsReturn(account);
        await _vm.OpenWizardCommand.ExecuteAsync(null);
        _vm.SelectAccountCommand.Execute(account);

        var filePath = CreateTempQfxFile(3);
        try
        {
            _vm.FileSelected(filePath);

            // First call succeeds, second fails, third succeeds
            var callCount = 0;
            _mockClient.StoreTransactionAsync(
                Arg.Any<Guid?>(),
                Arg.Any<TransactionStore>())
                .Returns(callInfo =>
                {
                    callCount++;
                    if (callCount == 2)
                        throw new HttpRequestException("Network error");
                    return Task.FromResult(new TransactionSingle());
                });

            ImportLogEntry? logEntry = null;
            _vm.ImportCompleted += (_, entry) => logEntry = entry;

            // Act
            await _vm.ConfirmImportCommand.ExecuteAsync(null);

            // Assert
            Assert.NotNull(logEntry);
            Assert.False(logEntry.Success);
            Assert.Equal(2, logEntry.TransactionCount); // 2 succeeded
            Assert.Contains("1 transaction(s) failed", logEntry.ErrorMessage);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 5.7: After import completes, the Results step is shown with Close and ImportAnother options.
    /// Verified by checking CurrentStep transitions to Results and both commands are available.
    /// </summary>
    [Fact]
    public async Task ResultsStepShowsCloseAndImportAnother()
    {
        // Arrange
        var account = CreateAccount("acc-1", "My Checking");
        SetupAccountsReturn(account);
        await _vm.OpenWizardCommand.ExecuteAsync(null);
        _vm.SelectAccountCommand.Execute(account);

        var filePath = CreateTempQfxFile(1);
        try
        {
            _vm.FileSelected(filePath);

            _mockClient.StoreTransactionAsync(
                Arg.Any<Guid?>(),
                Arg.Any<TransactionStore>(),
                Arg.Any<CancellationToken>())
                .Returns(new TransactionSingle());

            // Act
            await _vm.ConfirmImportCommand.ExecuteAsync(null);

            // Assert: wizard is on Results step
            Assert.Equal(WizardStep.Results, _vm.CurrentStep);

            // Assert: Close command works — transitions to Closed
            _vm.CloseCommand.Execute(null);
            Assert.Equal(WizardStep.Closed, _vm.CurrentStep);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 5.7 (continued): ImportAnother command transitions to FileSelection.
    /// </summary>
    [Fact]
    public async Task ResultsStepImportAnotherTransitionsToFileSelection()
    {
        // Arrange
        var account = CreateAccount("acc-1", "My Checking");
        SetupAccountsReturn(account);
        await _vm.OpenWizardCommand.ExecuteAsync(null);
        _vm.SelectAccountCommand.Execute(account);

        var filePath = CreateTempQfxFile(1);
        try
        {
            _vm.FileSelected(filePath);

            _mockClient.StoreTransactionAsync(
                Arg.Any<Guid?>(),
                Arg.Any<TransactionStore>(),
                Arg.Any<CancellationToken>())
                .Returns(new TransactionSingle());

            await _vm.ConfirmImportCommand.ExecuteAsync(null);
            Assert.Equal(WizardStep.Results, _vm.CurrentStep);

            // Act: ImportAnother
            _vm.ImportAnotherCommand.Execute(null);

            // Assert
            Assert.Equal(WizardStep.FileSelection, _vm.CurrentStep);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// Req 6.4: After navigating back from TransactionPreview and proceeding forward,
    /// the wizard opens the file picker (transitions to FileSelection step).
    /// </summary>
    [Fact]
    public async Task ForwardAfterBackOpensFilePicker()
    {
        // Arrange: get to TransactionPreview
        var account = CreateAccount("acc-1", "My Checking");
        SetupAccountsReturn(account);
        await _vm.OpenWizardCommand.ExecuteAsync(null);
        _vm.SelectAccountCommand.Execute(account);

        var filePath = CreateTempQfxFile(2);
        try
        {
            _vm.FileSelected(filePath);
            Assert.Equal(WizardStep.TransactionPreview, _vm.CurrentStep);

            // Act: go back
            _vm.GoBackCommand.Execute(null);
            Assert.Equal(WizardStep.AccountSelection, _vm.CurrentStep);
            // Account should be preserved
            Assert.Equal(account, _vm.SelectedAccount);

            // Act: proceed forward again
            _vm.ProceedToFileSelectionCommand.Execute(null);

            // Assert: transitions to FileSelection (where the View opens the file picker)
            Assert.Equal(WizardStep.FileSelection, _vm.CurrentStep);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
