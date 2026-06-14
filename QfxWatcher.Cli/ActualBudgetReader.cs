using Microsoft.Data.Sqlite;
using QfxWatcher.Models;

namespace QfxWatcher.Cli;

/// <summary>
/// Reads accounts and transactions from an Actual Budget SQLite database export.
/// Actual stores amounts as integer cents ($1.00 = 100, -$1.00 = -100).
/// Accounts and transactions use UUID-style string IDs.
/// </summary>
public sealed class ActualBudgetReader : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// Exposes the underlying connection for use by other readers (e.g. rule import).
    /// </summary>
    public SqliteConnection Connection => _connection;

    public ActualBudgetReader(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"SQLite database not found: {dbPath}", dbPath);

        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        _connection.Open();
    }

    /// <summary>
    /// Represents an account from the Actual Budget database.
    /// </summary>
    public record ActualAccount(string Id, string Name, bool Closed);

    /// <summary>
    /// Reads all non-tombstoned accounts from the database.
    /// </summary>
    public IReadOnlyList<ActualAccount> GetAccounts()
    {
        var accounts = new List<ActualAccount>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, closed
            FROM accounts
            WHERE tombstone = 0
            ORDER BY sort_order, name
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            accounts.Add(new ActualAccount(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Closed: reader.GetInt32(2) != 0
            ));
        }

        return accounts;
    }

    /// <summary>
    /// Reads all non-tombstoned transactions for a specific account.
    /// Converts Actual's integer-cents amounts to decimal dollars for FIIITransaction.
    /// 
    /// For transfers between accounts that are both in <paramref name="accountIdMapping"/>,
    /// only the outgoing (negative) side is returned to avoid double-importing.
    /// The opposing Firefly III account ID is set directly for reliable transfer creation.
    /// </summary>
    /// <param name="accountId">The Actual account ID to read transactions for.</param>
    /// <param name="accountIdMapping">
    /// Maps Actual account IDs to Firefly III account IDs for all accounts being imported.
    /// Used for transfer deduplication and direct account ID resolution.
    /// </param>
    public IReadOnlyList<FIIITransaction> GetTransactions(
        string accountId,
        IReadOnlyDictionary<string, string>? accountIdMapping = null)
    {
        var transactions = new List<FIIITransaction>();

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT
                t.id,
                t.date,
                t.amount,
                COALESCE(p.name, '') AS payee_name,
                COALESCE(t.imported_description, '') AS imported_description,
                COALESCE(t.notes, '') AS notes,
                t.transferred_id,
                COALESCE(c.name, '') AS category_name
            FROM transactions t
            LEFT JOIN payees p ON t.description = p.id
            LEFT JOIN categories c ON t.category = c.id
            WHERE t.acct = @accountId
              AND t.tombstone = 0
              AND t.starting_balance_flag = 0
            ORDER BY t.date
            """;

        cmd.Parameters.AddWithValue("@accountId", accountId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var dateInt = reader.GetInt32(1); // YYYYMMDD integer
            var amountCents = reader.GetInt32(2);
            var payeeName = reader.GetString(3);
            var importedDescription = reader.GetString(4);
            var notes = reader.GetString(5);
            var transferredId = reader.IsDBNull(6) ? null : reader.GetString(6);
            var categoryName = reader.GetString(7);

            // For transfers between two accounts that are both being imported,
            // only import the outgoing (negative) side to avoid duplicates.
            // Firefly III will create the matching entry in the destination account.
            string? opposingActualAccountId = null;
            if (transferredId != null)
            {
                opposingActualAccountId = GetAccountForTransaction(transferredId);
            }

            if (transferredId != null && accountIdMapping != null && opposingActualAccountId != null)
            {
                if (accountIdMapping.ContainsKey(opposingActualAccountId))
                {
                    // Both accounts are being imported — only import each transfer once.
                    // We always import from the outgoing (negative) side.
                    // Skip the incoming (positive) side regardless of ordering.
                    if (amountCents >= 0)
                        continue;
                }
            }

            // Actual stores dates as integer YYYYMMDD
            var date = DateOnly.ParseExact(dateInt.ToString(), "yyyyMMdd");

            // Convert cents to dollars
            var amount = amountCents / 100m;

            // Determine transaction type
            var transactionType = transferredId != null
                ? "XFER"
                : amount < 0 ? "DEBIT" : "CREDIT";

            // For transfers, resolve the opposing account name and Firefly III ID
            string? opposingName = null;
            string? opposingFireflyId = null;
            if (transferredId != null)
            {
                opposingName = GetAccountNameForTransaction(transferredId);

                // If we have a direct mapping to a Firefly III account ID, use it
                if (opposingActualAccountId != null && accountIdMapping != null)
                {
                    accountIdMapping.TryGetValue(opposingActualAccountId, out opposingFireflyId);
                }
            }

            // FireflyIIIService mapping:
            //   tx.Name  → transaction Description (what shows in Firefly III)
            //   tx.Memo  → opposing account name (destination for withdrawals, source for deposits)
            //   tx.OpposingAccountNumber → used for transfer account matching by ID
            //
            // For transfers: use opposing account name as Memo so FireflyIIIService can match it
            // For non-transfers: use payee name as Memo (becomes the expense/revenue account)
            var memo = transactionType == "XFER" && !string.IsNullOrWhiteSpace(opposingName)
                ? opposingName
                : payeeName;

            var combinedNotes = !string.IsNullOrWhiteSpace(notes)
                ? notes
                : !string.IsNullOrWhiteSpace(importedDescription)
                    ? importedDescription
                    : null;

            transactions.Add(new FIIITransaction
            {
                FitId = id,
                Date = date,
                Amount = amount,
                Name = !string.IsNullOrWhiteSpace(payeeName) ? payeeName : (opposingName ?? "(transfer)"),
                Memo = memo,
                TransactionType = transactionType,
                CategoryName = categoryName,
                Notes = combinedNotes,
                // Set opposing account number to Firefly III account ID for direct transfer matching
                OpposingAccountNumber = opposingFireflyId ?? string.Empty,
            });
        }

        return transactions;
    }

    /// <summary>
    /// Gets the account ID that a transaction belongs to.
    /// </summary>
    private string? GetAccountForTransaction(string transactionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT acct FROM transactions WHERE id = @id AND tombstone = 0";
        cmd.Parameters.AddWithValue("@id", transactionId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Gets the account name for the account that a transaction belongs to.
    /// </summary>
    private string? GetAccountNameForTransaction(string transactionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.name
            FROM transactions t
            JOIN accounts a ON t.acct = a.id
            WHERE t.id = @id AND t.tombstone = 0
            """;
        cmd.Parameters.AddWithValue("@id", transactionId);
        return cmd.ExecuteScalar() as string;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
