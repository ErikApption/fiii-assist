using QfxWatcher.FireflyIII;
using QfxWatcher.Services.Csv.Conversion;

namespace QfxWatcher.Services.Csv;

/// <summary>
/// Imports finalized CSV transactions into Firefly III via the generated API client.
/// </summary>
public sealed class CsvTransactionImporter
{
    private readonly Client _client;

    public CsvTransactionImporter(Client client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Import transactions from a processed CSV pipeline (FireflyTransaction objects).
    /// </summary>
    public async Task<int> ImportAsync(IReadOnlyList<FireflyTransaction> transactions)
    {
        if (transactions.Count == 0)
            return 0;

        var added = 0;

        foreach (var tx in transactions)
        {
            var split = new TransactionSplitStore
            {
                Type = tx.Type switch
                {
                    "deposit" => TransactionTypeProperty.Deposit,
                    "transfer" => TransactionTypeProperty.Transfer,
                    _ => TransactionTypeProperty.Withdrawal,
                },
                Date = DateTime.TryParse(tx.Date, out var d) ? d : DateTime.UtcNow,
                Amount = tx.Amount,
                Description = tx.Description,
                Source_id = tx.SourceId,
                Source_name = tx.SourceName,
                Destination_id = tx.DestinationId,
                Destination_name = tx.DestinationName,
                Currency_id = tx.CurrencyId,
                Currency_code = tx.CurrencyCode,
                Foreign_amount = tx.ForeignAmount,
                Foreign_currency_id = tx.ForeignCurrencyId,
                Foreign_currency_code = tx.ForeignCurrencyCode,
                Budget_id = tx.BudgetId,
                Budget_name = tx.BudgetName,
                Category_id = tx.CategoryId,
                Category_name = tx.CategoryName,
                Bill_id = tx.BillId,
                Bill_name = tx.BillName,
                Tags = tx.Tags,
                Notes = tx.Notes,
                External_id = tx.ExternalId,
                External_url = tx.ExternalUrl,
                Internal_reference = tx.InternalReference,
                Interest_date = ParseNullableDate(tx.InterestDate),
                Book_date = ParseNullableDate(tx.BookDate),
                Process_date = ParseNullableDate(tx.ProcessDate),
                Due_date = ParseNullableDate(tx.DueDate),
                Payment_date = ParseNullableDate(tx.PaymentDate),
                Invoice_date = ParseNullableDate(tx.InvoiceDate),
                Sepa_cc = tx.SepaCc,
                Sepa_ct_op = tx.SepaCtOp,
                Sepa_ct_id = tx.SepaCtId,
                Sepa_db = tx.SepaDb,
                Sepa_country = tx.SepaCountry,
                Sepa_ep = tx.SepaEp,
                Sepa_ci = tx.SepaCi,
                Sepa_batch_id = tx.SepaBatchId,
            };

            var payload = new TransactionStore
            {
                Error_if_duplicate_hash = tx.ErrorIfDuplicateHash,
                Apply_rules = tx.ApplyRules,
                Fire_webhooks = tx.FireWebhooks,
                Transactions = [split],
            };

            _ = await _client.StoreTransactionAsync(null, payload);
            added++;
        }

        return added;
    }

    private static DateTimeOffset? ParseNullableDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, out var dt) ? dt : null;
    }
}
