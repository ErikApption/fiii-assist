using System.Linq;
using NSubstitute;
using QfxWatcher.FireflyIII;
using QfxWatcher.Models;
using QfxWatcher.Services;

namespace QfxWatcher.Core.Tests;

/// <summary>
/// Tests that EFT withdrawals with transfer-like wording in the NAME/MEMO fields
/// are correctly classified as withdrawals (not transfers) when imported from QFX.
/// 
/// Regression: Previously, an "EFT Withdrawal to Tangerine Fund" with TRNTYPE=DEBIT
/// would be incorrectly detected as a transfer because the NAME matched a pattern
/// like "Withdrawal to {account}". This caused a duplicate transaction — the bank
/// statement also contained the real XFER entry, resulting in two transfers.
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

    public void Dispose()
    {
        _service.Dispose();
    }

    /// <summary>
    /// An EFT withdrawal with TRNTYPE=DEBIT and NAME="EFT Withdrawal to Tangerine Fund"
    /// should produce a single Withdrawal transaction, NOT a Transfer — even though
    /// "Tangerine Fund" is a known asset account.
    /// 
    /// This is the exact data from a real Tangerine Bank QFX file.
    /// </summary>
    [Fact]
    public async Task EftWithdrawal_WithDebitType_CreatesWithdrawalNotTransfer()
    {
        // Arrange: exact transaction from a real QFX file
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

        // Act
        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // Assert: exactly one transaction was imported
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);

        // Assert: the transaction was created as a Withdrawal, not a Transfer
        var stored = _capturedTransactions[0];
        var split = stored.Transactions.First();
        Assert.Equal(TransactionTypeProperty.Withdrawal, split.Type);
        Assert.Equal("100", split.Source_id);

        // Assert: destination_name must NOT be "Tangerine Fund" because that matches
        // a known asset account and Firefly III would auto-promote it to a Transfer.
        // Instead, the full NAME field is used as the expense account name.
        Assert.NotEqual("Tangerine Fund", split.Destination_name);
        Assert.Equal("EFT Withdrawal to Tangerine Fund", split.Destination_name);
    }

    /// <summary>
    /// An EFT deposit with TRNTYPE=CREDIT and NAME="EFT Deposit from Apption Corpora"
    /// should produce a single Deposit, not a Transfer — even if the memo says "From Apption".
    /// </summary>
    [Fact]
    public async Task EftDeposit_WithCreditType_CreatesDepositNotTransfer()
    {
        var transaction = new FIIITransaction
        {
            FitId = "6400",
            TransactionType = "CREDIT",
            Date = new DateOnly(2026, 3, 31),
            Amount = 8427.29m,
            Name = "EFT Deposit from Apption Corpora",
            Memo = "From Apption Corpora",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);

        var split = _capturedTransactions[0].Transactions.First();
        Assert.Equal(TransactionTypeProperty.Deposit, split.Type);
        Assert.Equal("100", split.Destination_id);
    }

    /// <summary>
    /// A real XFER transaction (TRNTYPE=XFER) to a known account SHOULD still create a Transfer.
    /// This ensures we didn't break legitimate transfer detection.
    /// </summary>
    [Fact]
    public async Task RealXferTransaction_ToKnownAccount_CreatesTransfer()
    {
        // Add "Tangerine Savings Account" as a known account
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
        accountArray.Data.Add(new AccountRead
        {
            Id = "300",
            Type = "accounts",
            Attributes = new AccountProperties
            {
                Name = "Tangerine Savings Account",
                Account_number = "11111",
                Type = ShortAccountTypeProperty.Asset,
            }
        });

        SetupListAccountMock(accountArray);

        var transaction = new FIIITransaction
        {
            FitId = "6386",
            TransactionType = "XFER",
            Date = new DateOnly(2026, 1, 28),
            Amount = -150.00m,
            Name = "Deposit from Tangerine Savings Account",
            Memo = "Transfer",
            OpposingAccountNumber = string.Empty,
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // XFER type should still produce a Transfer
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);

        var split = _capturedTransactions[0].Transactions.First();
        Assert.Equal(TransactionTypeProperty.Transfer, split.Type);
    }

    /// <summary>
    /// A DEBIT transaction with BANKACCTTO (opposing account number) that matches a known
    /// account SHOULD create a Transfer — the bank explicitly told us where the money went.
    /// </summary>
    [Fact]
    public async Task DebitWithBankAcctTo_MatchingKnownAccount_CreatesTransfer()
    {
        var transaction = new FIIITransaction
        {
            FitId = "6387",
            TransactionType = "DEBIT",
            Date = new DateOnly(2026, 1, 28),
            Amount = -150.00m,
            Name = "EFT Withdrawal to Tangerine Fund",
            Memo = "To Tangerine Fund",
            // Bank included BANKACCTTO with the account number
            OpposingAccountNumber = "67890",
        };

        var imported = await _service.ImportTransactionsAsync("100", [transaction]);

        // Opposing account number matches "Tangerine Fund" (account_number=67890)
        // → should be a Transfer
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);

        var split = _capturedTransactions[0].Transactions.First();
        Assert.Equal(TransactionTypeProperty.Transfer, split.Type);
    }

    /// <summary>
    /// End-to-end: parse the exact QFX content from a real Tangerine Bank file containing
    /// an EFT withdrawal, then import it. Should produce exactly one Withdrawal.
    /// </summary>
    [Fact]
    public async Task EndToEnd_ParseAndImport_EftWithdrawalProducesSingleWithdrawal()
    {
        // Arrange: exact QFX content from the bank
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

        // Parse QFX
        var transactions = QfxParserService.Parse(qfxContent);

        // Verify parse produced exactly one transaction
        var tx = Assert.Single(transactions);
        Assert.Equal("DEBIT", tx.TransactionType);
        Assert.Equal(-150.00m, tx.Amount);
        Assert.Equal("EFT Withdrawal to Tangerine Fund", tx.Name);
        Assert.Equal("To Tangerine Fund", tx.Memo);

        // Import it
        var imported = await _service.ImportTransactionsAsync("100", transactions.ToList());

        // Assert: exactly one API call, and it's a Withdrawal
        Assert.Equal(1, imported);
        Assert.Single(_capturedTransactions);

        var split = _capturedTransactions[0].Transactions.First();
        Assert.Equal(TransactionTypeProperty.Withdrawal, split.Type);
        Assert.Equal("100", split.Source_id);

        // The destination must NOT be "Tangerine Fund" (an asset account name) —
        // that would cause Firefly III to auto-promote to Transfer, creating a duplicate.
        Assert.NotEqual("Tangerine Fund", split.Destination_name);
        Assert.Equal("EFT Withdrawal to Tangerine Fund", split.Destination_name);
    }
}
