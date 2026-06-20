using System.Globalization;
using System.Linq;
using NSubstitute;
using FiiiAssist.FireflyIII;
using FiiiAssist.Models;
using FiiiAssist.Services;

namespace FiiiAssist.Core.Tests;

/// <summary>
/// Tests that EFT withdrawals to known accounts are correctly created as transfers,
/// and that duplicate transfers are detected and skipped.
///
/// Regression: Previously, importing an EFT withdrawal from QFX created a duplicate transfer
/// if the same movement had already been imported from another source (e.g. Actual Budget).
/// The fix adds transfer deduplication by date + amount + opposing account before import.
/// </summary>
public class FireflyIIIServiceTransferTests : IDisposable
{
    private readonly Client _mockClient;
    private readonly FireflyIIIService _service;
    private readonly List<TransactionStore> _capturedTransactions = new();

    public FireflyIIIServiceTransferTests()
    {
        _mockClient = Substitute.For<Client>(new HttpClient());
        _service = new FireflyIIIService(_mockClient);

        // Setup: return Tangerine Checking and Tangerine Fund as known asset accounts
        var accountArray = CreateDefaultAccountArray();
        SetupListAccountMock(accountArray);
        SetupEmptyTransferList();

        // Capture all StoreTransactionAsync calls for assertion
        _mockClient.StoreTransactionAsync(
            Arg.Any<Guid?>(),
            Arg.Any<TransactionStore>())
            .Returns(callInfo =>
            {
                _capturedTransactions.Add(callInfo.ArgAt<TransactionStore>(1));
                return new TransactionSingle();
            });
    }

    private static AccountArray CreateDefaultAccountArray()
    {
        var accountArray = new AccountArray();
        accountArray.Data.Add(new AccountRead
        {
            Id = "100",
            Type = "accounts",
            Attributes = new AccountProperties
            {
                Name = "Tangerine Checking",
                Account_number = "12345",
                Type = ShortAccountTypeProperty.Asset,
            }
        });
        accountArray.Data.Add(new AccountRead
        {
            Id = "200",
            Type = "accounts",
            Attributes = new AccountProperties
            {
                Name = "Tangerine Fund",
                Account_number = "67890",
                Type = ShortAccountTypeProperty.Asset,
            }
        });
        return accountArray;
    }

    private void SetupListAccountMock(AccountArray accountArray)
    {
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

    /// <summary>
    /// Sets up the mock to return no existing transfers (clean slate).
    /// </summary>
    private void SetupEmptyTransferList()
    {
        _mockClient.ListTransactionByAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<TransactionTypeFilter?>())
            .Returns(new TransactionArray());

        _mockClient.ListTransactionByAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<TransactionTypeFilter?>(),
            Arg.Any<CancellationToken>())
            .Returns(new TransactionArray());
    }

    /// <summary>
    /// Sets up the mock to return an existing transfer (simulating a prior import).
    /// </summary>
    private void SetupExistingTransfer(string sourceId, string destId, DateOnly date, decimal amount)
    {
        var txArray = new TransactionArray();
        var txRead = new TransactionRead
        {
            Id = "existing-1",
            Type = "transactions",
            Attributes = new Transaction()
        };
        txRead.Attributes.Transactions.Add(new TransactionSplit
        {
            Type = TransactionTypeProperty.Transfer,
            Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
            Source_id = sourceId,
            Destination_id = destId,
            Description = "Transfer",
        });
        txArray.Data.Add(txRead);

        _mockClient.ListTransactionByAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Is<TransactionTypeFilter?>(t => t == TransactionTypeFilter.Transfer))
            .Returns(txArray);

        _mockClient.ListTransactionByAccountAsync(
            Arg.Any<Guid?>(),
            Arg.Any<int?>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Any<DateTimeOffset?>(),
            Arg.Is<TransactionTypeFilter?>(t => t == TransactionTypeFilter.Transfer),
            Arg.Any<CancellationToken>())
            .Returns(txArray);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    /// <summary>
    /// An EFT withdrawal with MEMO="To Tangerine Fund" where Tangerine Fund is a known
    /// asset account should create a transfer (Firefly III auto-promotes based on the
    /// destination matching an asset account).
    /// When no prior transfer exists, the import should succeed.
    /// </summary>
    [Fact]
    public async Task EftWithdrawal_ToKnownAccount_NoPriorTransfer_ImportsSuccessfully()
    {
        var transaction = new FIIITransaction
        {
            FitId = "6385",
            TransactionType = "DEBIT",
            Date = new DateOnly(2026, 1, 28),
            Amount = -150.00m,
            Name = "EFT Withdrawal to Tangerine Fund",
            Memo = "To Tangerine Fund",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // Should import successfully (one API call)
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);
    }

    /// <summary>
    /// When an identical transfer already exists in Firefly III (same date, amount,
    /// and opposing account), importing the same EFT withdrawal should be skipped.
    /// This prevents duplicates when importing from multiple sources (QFX + Actual Budget).
    /// </summary>
    [Fact]
    public async Task EftWithdrawal_ToKnownAccount_WithExistingTransfer_IsSkipped()
    {
        // Arrange: simulate that a transfer from account 100 to 200 for $150 on 2026-01-28
        // already exists (e.g. imported from Actual Budget previously)
        SetupExistingTransfer("100", "200", new DateOnly(2026, 1, 28), 150.00m);

        var transaction = new FIIITransaction
        {
            FitId = "6385",
            TransactionType = "DEBIT",
            Date = new DateOnly(2026, 1, 28),
            Amount = -150.00m,
            Name = "EFT Withdrawal to Tangerine Fund",
            Memo = "To Tangerine Fund",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // Should be skipped — no API call made
        Assert.Equal(0, imported);
        Assert.Empty(_capturedTransactions);
    }

    /// <summary>
    /// A different amount on the same date should NOT be skipped (it's a different transaction).
    /// </summary>
    [Fact]
    public async Task EftWithdrawal_DifferentAmount_IsNotSkipped()
    {
        // Existing: $150 transfer on 2026-01-28
        SetupExistingTransfer("100", "200", new DateOnly(2026, 1, 28), 150.00m);

        // New: $200 transfer on the same date
        var transaction = new FIIITransaction
        {
            FitId = "6400",
            TransactionType = "DEBIT",
            Date = new DateOnly(2026, 1, 28),
            Amount = -200.00m,
            Name = "EFT Withdrawal to Tangerine Fund",
            Memo = "To Tangerine Fund",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // Should import (different amount)
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);
    }

    /// <summary>
    /// A withdrawal to an expense account (not a known asset account) should not
    /// be affected by transfer deduplication.
    /// </summary>
    [Fact]
    public async Task RegularWithdrawal_ToExpenseAccount_IsNeverSkipped()
    {
        // Even with existing transfers, a regular withdrawal should import
        SetupExistingTransfer("100", "200", new DateOnly(2026, 1, 28), 50.00m);

        var transaction = new FIIITransaction
        {
            FitId = "6401",
            TransactionType = "DEBIT",
            Date = new DateOnly(2026, 1, 28),
            Amount = -50.00m,
            Name = "Tim Hortons",
            Memo = "Coffee",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // "Coffee" doesn't match any asset account — should import normally
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);
    }

    /// <summary>
    /// End-to-end: parse the exact QFX content from a real Tangerine Bank file,
    /// with a prior transfer already existing. The import should be skipped.
    /// </summary>
    [Fact]
    public async Task EndToEnd_ParseAndImport_DuplicateTransferIsSkipped()
    {
        // Simulate existing transfer from prior Actual Budget import
        SetupExistingTransfer("100", "200", new DateOnly(2026, 1, 28), 150.00m);

        var qfxContent = """
            OFXHEADER:100
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
            <DTSERVER>20260128120000
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
            <CURDEF>CAD
            <BANKACCTFROM>
            <BANKID>614
            <ACCTID>12345
            <ACCTTYPE>CHECKING
            </BANKACCTFROM>
            <BANKTRANLIST>
            <DTSTART>20260128120000
            <DTEND>20260128120000
            <STMTTRN>
            <TRNTYPE>DEBIT
            <DTPOSTED>20260128120000.000
            <TRNAMT>-150.00
            <FITID>6385
            <NAME>EFT Withdrawal to Tangerine Fund
            <MEMO>To Tangerine Fund
            </STMTTRN>
            </BANKTRANLIST>
            </STMTRS>
            </STMTTRNRS>
            </BANKMSGSRSV1>
            </OFX>
            """;

        var transactions = QfxParserService.Parse(qfxContent);
        var tx = Assert.Single(transactions);
        Assert.Equal("DEBIT", tx.TransactionType);
        Assert.Equal(-150.00m, tx.Amount);

        var imported = await _service.ImportTransactionsAsync("100", transactions.ToList());

        // Duplicate transfer detected — should be skipped
        Assert.Equal(0, imported);
        Assert.Empty(_capturedTransactions);
    }
}
