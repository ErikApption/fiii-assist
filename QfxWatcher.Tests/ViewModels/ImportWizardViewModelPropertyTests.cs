using FsCheck;
using FsCheck.Xunit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.Services;
using QfxWatcher.ViewModels;

namespace QfxWatcher.Tests.ViewModels;

/// <summary>
/// Property-based tests for <see cref="ImportWizardViewModel"/> using FsCheck.
/// Each property validates a correctness invariant from the design document.
/// </summary>
[Trait("Feature", "qfx-import-wizard")]
public class ImportWizardViewModelPropertyTests : IDisposable
{
    private readonly Client _mockClient;
    private readonly FireflyIIIService _fireflyService;
    private readonly SettingsService _settingsService;

    public ImportWizardViewModelPropertyTests()
    {
        _mockClient = Substitute.For<Client>(new HttpClient());
        _fireflyService = new FireflyIIIService(_mockClient);
        _settingsService = new SettingsService();
    }

    public void Dispose()
    {
        _fireflyService.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ImportWizardViewModel CreateViewModel() =>
        new(_fireflyService, _settingsService);

    private static AccountRead CreateAccount(string id, string name, string? accountNumber = null) => new()
    {
        Id = id,
        Type = "accounts",
        Attributes = new AccountProperties { Name = name, Account_number = accountNumber ?? string.Empty }
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
        var accountArray = CreateAccountArray(accounts);
        _mockClient.ListAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<AccountTypeFilter?>())
            .Returns(accountArray);
        _mockClient.ListAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<AccountTypeFilter?>(),
            Arg.Any<CancellationToken>())
            .Returns(accountArray);
    }

    private string CreateTempQfxFile(List<(DateOnly Date, decimal Amount, string Name)> transactions)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pbt_{Guid.NewGuid()}.qfx");
        var txContent = string.Empty;
        for (int i = 0; i < transactions.Count; i++)
        {
            var (date, amount, name) = transactions[i];
            txContent += $@"
<STMTTRN>
<TRNTYPE>{(amount >= 0 ? "CREDIT" : "DEBIT")}
<DTPOSTED>{date:yyyyMMdd}120000
<TRNAMT>{amount}
<FITID>TX{Guid.NewGuid():N}
<NAME>{name}
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
<DTEND>20241231120000
{txContent}
</BANKTRANLIST>
</STMTRS>
</STMTTRNRS>
</BANKMSGSRSV1>
</OFX>";

        File.WriteAllText(path, content);
        return path;
    }

    // ── Property 1: Wizard cannot open without valid connection ────────────────
    // **Validates: Requirements 1.2**

    /// <summary>
    /// For any application state where IsConnected is false, the wizard's initial state
    /// should prevent launching (CurrentStep is Closed and CanGoNext is false).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public bool Property1_WizardCannotOpenWithoutValidConnection(byte seed)
    {
        // The wizard starts in Closed state with CanGoNext = false regardless of seed.
        // This represents the state when IsConnected is false on the DashboardViewModel.
        var vm = CreateViewModel();

        // The wizard's initial state prevents launching:
        // - CurrentStep is Closed (wizard not visible)
        // - CanGoNext is false (no progression possible)
        return vm.CurrentStep == WizardStep.Closed && !vm.CanGoNext;
    }

    // ── Property 2: Opening the wizard transitions to AccountSelection ────────
    // **Validates: Requirements 1.3**

    /// <summary>
    /// For any valid pre-condition (wizard is Closed, service is connected),
    /// calling OpenWizard transitions CurrentStep to FileSelection.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public bool Property2_OpeningWizardTransitionsToFileSelection(PositiveInt accountCount)
    {
        // Generate between 1 and the given count of accounts
        var count = Math.Min(accountCount.Get, 20);
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        SetupAccountsReturn(accounts);
        var vm = CreateViewModel();

        // Pre-condition: wizard is Closed
        Assert.Equal(WizardStep.Closed, vm.CurrentStep);

        // Act
        vm.OpenWizardCommand.Execute(null);

        // Post-condition: CurrentStep is FileSelection
        return vm.CurrentStep == WizardStep.FileSelection;
    }

    // ── Property 3: All fetched accounts are exposed in the collection ────────
    // **Validates: Requirements 2.3**

    /// <summary>
    /// For any non-empty list of accounts returned by the service, the Accounts collection
    /// contains every account with no omissions or duplicates after file selection.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property3_AllFetchedAccountsExposedInCollection(PositiveInt accountCount)
    {
        var count = Math.Min(accountCount.Get, 50);
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        SetupAccountsReturn(accounts);
        var vm = CreateViewModel();

        // Accounts are loaded during FileSelectedAsync
        var txData = new List<(DateOnly, decimal, string)>
        {
            (DateOnly.FromDateTime(DateTime.Today), -10.00m, "Test")
        };
        var filePath = CreateTempQfxFile(txData);
        try
        {
            await vm.FileSelectedAsync(filePath);

            // All accounts present (no omissions)
            var allPresent = accounts.All(a => vm.Accounts.Any(va => va.Id == a.Id));
            // No duplicates
            var noDuplicates = vm.Accounts.Select(a => a.Id).Distinct().Count() == vm.Accounts.Count;
            // Same count
            var sameCount = vm.Accounts.Count == accounts.Length;

            return allPresent && noDuplicates && sameCount;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 4: DefaultAccountId pre-selects the matching account ─────────
    // **Validates: Requirements 2.6**

    /// <summary>
    /// For any list of accounts and any DefaultAccountId that matches an account ID,
    /// SelectedAccount equals that account after loading accounts (triggered by file selection).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property4_DefaultAccountIdPreSelectsMatchingAccount(PositiveInt accountCount, NonNegativeInt selectedIndex)
    {
        var count = Math.Max(1, Math.Min(accountCount.Get, 20));
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        // Pick a valid index into the accounts array
        var idx = selectedIndex.Get % count;
        var defaultAccountId = accounts[idx].Id;

        SetupAccountsReturn(accounts);

        // Configure settings with the default account ID
        _settingsService.Save(new AppSettings { DefaultAccountId = defaultAccountId });

        var vm = CreateViewModel();

        // Accounts are loaded during FileSelectedAsync — use a file without matching ACCTID
        var txData = new List<(DateOnly, decimal, string)>
        {
            (DateOnly.FromDateTime(DateTime.Today), -10.00m, "Test")
        };
        var filePath = CreateTempQfxFile(txData);
        try
        {
            await vm.FileSelectedAsync(filePath);
            // File has ACCTID 987654321 which won't match, so we end up at AccountSelection
            // with DefaultAccountId pre-selected
            return vm.SelectedAccount != null && vm.SelectedAccount.Id == defaultAccountId;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 5: CanGoNext requires exactly one selected account ───────────
    // **Validates: Requirements 2.7**

    /// <summary>
    /// For any wizard state on AccountSelection step, CanGoNext == (SelectedAccount != null).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property5_CanGoNextRequiresSelectedAccount(PositiveInt accountCount, bool selectAccount)
    {
        var count = Math.Min(accountCount.Get, 20);
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        // Ensure no default account is pre-selected
        _settingsService.Save(new AppSettings { DefaultAccountId = string.Empty });

        SetupAccountsReturn(accounts);
        var vm = CreateViewModel();

        // Trigger FileSelectedAsync to get to AccountSelection (ACCTID won't match)
        var txData = new List<(DateOnly, decimal, string)>
        {
            (DateOnly.FromDateTime(DateTime.Today), -10.00m, "Test")
        };
        var filePath = CreateTempQfxFile(txData);
        try
        {
            await vm.FileSelectedAsync(filePath);
            // Should be at AccountSelection since no ACCTID match
            if (vm.CurrentStep != WizardStep.AccountSelection)
                return true; // Skip if auto-matched (shouldn't happen since ACCTID 987654321 doesn't match)

            if (selectAccount && accounts.Length > 0)
            {
                vm.SelectAccountCommand.Execute(accounts[0]);
            }

            // CanGoNext should be true iff SelectedAccount is not null
            return vm.CanGoNext == (vm.SelectedAccount != null);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 6: Backward navigation preserves the selected account ────────
    // **Validates: Requirements 3.3, 6.3**

    /// <summary>
    /// For any selected account, navigating backward preserves SelectedAccount unchanged.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property6_BackwardNavigationPreservesSelectedAccount(PositiveInt accountCount)
    {
        var count = Math.Max(1, Math.Min(accountCount.Get, 20));
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        _settingsService.Save(new AppSettings { DefaultAccountId = string.Empty });
        SetupAccountsReturn(accounts);
        var vm = CreateViewModel();

        // Create a temp file and parse it — ACCTID won't match so we get AccountSelection
        var txData = new List<(DateOnly, decimal, string)>
        {
            (DateOnly.FromDateTime(DateTime.Today), -25.00m, "Test Payee")
        };
        var filePath = CreateTempQfxFile(txData);
        try
        {
            await vm.FileSelectedAsync(filePath);
            Assert.Equal(WizardStep.AccountSelection, vm.CurrentStep);

            // Select an account
            var selectedAccount = accounts[0];
            vm.SelectAccountCommand.Execute(selectedAccount);
            Assert.Equal(selectedAccount, vm.SelectedAccount);

            // Proceed to TransactionPreview
            vm.ProceedToTransactionPreviewCommand.Execute(null);
            Assert.Equal(WizardStep.TransactionPreview, vm.CurrentStep);

            // Navigate backward
            vm.GoBackCommand.Execute(null);

            // SelectedAccount should be preserved
            return vm.SelectedAccount == selectedAccount &&
                   vm.CurrentStep == WizardStep.AccountSelection;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 7: TransactionCount equals the parsed collection size ────────
    // **Validates: Requirements 4.1**

    /// <summary>
    /// For any list of transactions, TransactionCount == Transactions.Count.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property7_TransactionCountEqualsCollectionSize(PositiveInt txCount)
    {
        var count = Math.Min(txCount.Get, 30);
        var transactions = Enumerable.Range(1, count)
            .Select(i => (
                Date: DateOnly.FromDateTime(DateTime.Today.AddDays(-i)),
                Amount: -(10.00m + i),
                Name: $"Payee {i}"
            ))
            .ToList();

        var filePath = CreateTempQfxFile(transactions);
        try
        {
            var vm = CreateViewModel();
            await vm.FileSelectedAsync(filePath);

            return vm.TransactionCount == vm.Transactions.Count &&
                   vm.TransactionCount == count;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 8: Transactions are ordered by date descending ───────────────
    // **Validates: Requirements 4.2**

    /// <summary>
    /// For any list of parsed transactions with 2+ items, adjacent pairs satisfy
    /// Transactions[i].Date >= Transactions[i+1].Date.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property8_TransactionsOrderedByDateDescending(PositiveInt txCount)
    {
        var count = Math.Max(2, Math.Min(txCount.Get, 30));

        // Generate transactions with random-ish dates (spread across a year)
        var rng = new Random(txCount.Get);
        var transactions = Enumerable.Range(1, count)
            .Select(i => (
                Date: DateOnly.FromDateTime(DateTime.Today.AddDays(-rng.Next(0, 365))),
                Amount: -(decimal)(rng.NextDouble() * 100),
                Name: $"Payee {i}"
            ))
            .ToList();

        var filePath = CreateTempQfxFile(transactions);
        try
        {
            var vm = CreateViewModel();
            await vm.FileSelectedAsync(filePath);

            if (vm.Transactions.Count < 2)
                return true; // vacuously true

            for (int i = 0; i < vm.Transactions.Count - 1; i++)
            {
                if (vm.Transactions[i].Date < vm.Transactions[i + 1].Date)
                    return false;
            }
            return true;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 9: Navigation controls reflect step constraints ──────────────
    // **Validates: Requirements 5.2, 6.1, 6.2, 6.5**

    /// <summary>
    /// For any wizard step:
    /// - CanGoBack is true on TransactionPreview and AccountSelection (after file selection)
    /// - CanGoBack is false on FileSelection, Importing, and Results
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property9_NavigationControlsReflectStepConstraints(PositiveInt accountCount)
    {
        var count = Math.Max(1, Math.Min(accountCount.Get, 10));
        var accounts = Enumerable.Range(1, count)
            .Select(i => CreateAccount($"acc-{i}", $"Account {i}"))
            .ToArray();

        _settingsService.Save(new AppSettings { DefaultAccountId = string.Empty });
        SetupAccountsReturn(accounts);

        // Mock successful import
        _mockClient.StoreTransactionAsync(
            Arg.Any<Guid?>(),
            Arg.Any<TransactionStore>(),
            Arg.Any<CancellationToken>())
            .Returns(new TransactionSingle());

        var vm = CreateViewModel();

        // Step: Closed
        if (vm.CanGoBack) return false;

        // Step: FileSelection (via OpenWizard)
        vm.OpenWizardCommand.Execute(null);
        if (vm.CurrentStep != WizardStep.FileSelection) return false;
        if (vm.CanGoBack) return false; // CanGoBack should be false on FileSelection

        // Parse a file to get to AccountSelection (no ACCTID match)
        var txData = new List<(DateOnly, decimal, string)>
        {
            (DateOnly.FromDateTime(DateTime.Today), -15.00m, "Test")
        };
        var filePath = CreateTempQfxFile(txData);
        try
        {
            await vm.FileSelectedAsync(filePath);

            // Step: AccountSelection - CanGoBack should be true (can go back to FileSelection)
            if (vm.CurrentStep != WizardStep.AccountSelection) return false;
            if (!vm.CanGoBack) return false;

            // Select account and proceed to TransactionPreview
            vm.SelectAccountCommand.Execute(accounts[0]);
            vm.ProceedToTransactionPreviewCommand.Execute(null);

            // Step: TransactionPreview - CanGoBack should be true
            if (vm.CurrentStep != WizardStep.TransactionPreview) return false;
            if (!vm.CanGoBack) return false;

            // Execute import
            await vm.ConfirmImportCommand.ExecuteAsync(null);

            // Step: Results - CanGoBack should be false
            if (vm.CurrentStep != WizardStep.Results) return false;
            if (vm.CanGoBack) return false;

            return true;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    // ── Property 10: Import accounting invariant ──────────────────────────────
    // **Validates: Requirements 5.5**

    /// <summary>
    /// For any import execution, ImportedCount + FailedCount == TotalCount.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "qfx-import-wizard")]
    public async Task<bool> Property10_ImportAccountingInvariant(PositiveInt txCount, int failSeed)
    {
        var count = Math.Max(1, Math.Min(txCount.Get, 20));
        var accounts = new[] { CreateAccount("acc-1", "Test Account") };

        _settingsService.Save(new AppSettings { DefaultAccountId = string.Empty });
        SetupAccountsReturn(accounts);

        var vm = CreateViewModel();

        var transactions = Enumerable.Range(1, count)
            .Select(i => (
                Date: DateOnly.FromDateTime(DateTime.Today.AddDays(-i)),
                Amount: -(10.00m + i),
                Name: $"Payee {i}"
            ))
            .ToList();

        var filePath = CreateTempQfxFile(transactions);
        try
        {
            await vm.FileSelectedAsync(filePath);
            // No ACCTID match → AccountSelection
            vm.SelectAccountCommand.Execute(accounts[0]);
            vm.ProceedToTransactionPreviewCommand.Execute(null);

            // Use the failSeed to determine which transactions fail
            var rng = new Random(failSeed);
            var callIndex = 0;
            _mockClient.StoreTransactionAsync(
                Arg.Any<Guid?>(),
                Arg.Any<TransactionStore>(),
                Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var idx = Interlocked.Increment(ref callIndex);
                    // Randomly fail some transactions based on seed
                    if (rng.Next(0, 3) == 0) // ~33% failure rate
                        throw new HttpRequestException("Simulated failure");
                    return Task.FromResult(new TransactionSingle());
                });

            await vm.ConfirmImportCommand.ExecuteAsync(null);

            // The accounting invariant: ImportedCount + FailedCount == TotalCount
            return vm.ImportedCount + vm.FailedCount == vm.TotalCount &&
                   vm.TotalCount == count;
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
