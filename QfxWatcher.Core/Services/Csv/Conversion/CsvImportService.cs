using System.Globalization;
using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Services.Csv.Conversion;

/// <summary>
/// High-level service that orchestrates the full CSV import pipeline:
/// CSV content → parsed lines → column values → pseudo-transactions → finalized transactions.
/// </summary>
public sealed class CsvImportService
{
    private readonly CsvImportConfiguration _config;

    public CsvImportService(CsvImportConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Process CSV content through the full pipeline and return finalized PseudoTransactions
    /// ready for submission to Firefly III.
    /// </summary>
    public List<PseudoTransaction> ProcessCsv(string csvContent)
    {
        // Step 1: Parse and sanitize CSV
        var fileProcessor = new CsvFileProcessor(_config);
        var lines = fileProcessor.ProcessCsvContent(csvContent);

        // Step 2: Convert lines to ColumnValue arrays
        var lineProcessor = new LineProcessor(_config);
        var columnValueLines = lineProcessor.ProcessLines(lines);

        // Step 3: Convert ColumnValues to PseudoTransactions
        var columnValueConverter = new ColumnValueConverter(_config);
        var pseudoTransactions = columnValueConverter.ProcessLines(columnValueLines);

        // Step 4: Finalize transactions (resolve amounts, tags, accounts, etc.)
        var transactionProcessor = new TransactionProcessor(_config.DefaultAccountId);
        return transactionProcessor.Process(pseudoTransactions);
    }

    /// <summary>
    /// Converts finalized PseudoTransactions into Firefly III API-compatible transaction payloads.
    /// Returns a list of JSON-serializable dictionaries matching the Firefly III transaction store format.
    /// </summary>
    public static List<FireflyTransaction> ToFireflyTransactions(List<PseudoTransaction> transactions)
    {
        return transactions
            .Where(tx => tx.Amount.HasValue && tx.Amount.Value != 0m)
            .Select(ToFireflyTransaction)
            .ToList();
    }

    private static FireflyTransaction ToFireflyTransaction(PseudoTransaction tx)
    {
        return new FireflyTransaction
        {
            ErrorIfDuplicateHash = tx.ErrorIfDuplicateHash,
            ApplyRules = tx.ApplyRules,
            FireWebhooks = tx.FireWebhooks,
            Type = tx.Type,
            Date = tx.Date ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Amount = (tx.Amount ?? 0m).ToString(CultureInfo.InvariantCulture),
            Description = tx.Description ?? "(no description)",
            SourceId = tx.SourceId?.ToString(),
            SourceName = tx.SourceName,
            SourceIban = tx.SourceIban,
            SourceNumber = tx.SourceNumber,
            DestinationId = tx.DestinationId?.ToString(),
            DestinationName = tx.DestinationName,
            DestinationIban = tx.DestinationIban,
            DestinationNumber = tx.DestinationNumber,
            CurrencyId = tx.CurrencyId?.ToString(),
            CurrencyCode = tx.CurrencyCode,
            ForeignAmount = tx.ForeignAmount?.ToString(CultureInfo.InvariantCulture),
            ForeignCurrencyId = tx.ForeignCurrencyId?.ToString(),
            ForeignCurrencyCode = tx.ForeignCurrencyCode,
            BudgetId = tx.BudgetId?.ToString(),
            BudgetName = tx.BudgetName,
            CategoryId = tx.CategoryId?.ToString(),
            CategoryName = tx.CategoryName,
            BillId = tx.BillId?.ToString(),
            BillName = tx.BillName,
            Tags = tx.Tags,
            Notes = tx.Notes,
            ExternalId = tx.ExternalId,
            ExternalUrl = tx.ExternalUrl,
            InternalReference = tx.InternalReference,
            InterestDate = tx.InterestDate,
            BookDate = tx.BookDate,
            ProcessDate = tx.ProcessDate,
            DueDate = tx.DueDate,
            PaymentDate = tx.PaymentDate,
            InvoiceDate = tx.InvoiceDate,
            SepaCc = tx.SepaCc,
            SepaCtOp = tx.SepaCtOp,
            SepaCtId = tx.SepaCtId,
            SepaDb = tx.SepaDb,
            SepaCountry = tx.SepaCountry,
            SepaEp = tx.SepaEp,
            SepaCi = tx.SepaCi,
            SepaBatchId = tx.SepaBatchId,
        };
    }
}

/// <summary>
/// A finalized transaction ready for submission to the Firefly III API.
/// </summary>
public sealed class FireflyTransaction
{
    public bool ErrorIfDuplicateHash { get; set; }
    public bool ApplyRules { get; set; }
    public bool FireWebhooks { get; set; }

    public string Type { get; set; } = "withdrawal";
    public string Date { get; set; } = "";
    public string Amount { get; set; } = "0";
    public string Description { get; set; } = "";

    public string? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceIban { get; set; }
    public string? SourceNumber { get; set; }
    public string? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public string? DestinationIban { get; set; }
    public string? DestinationNumber { get; set; }

    public string? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public string? ForeignAmount { get; set; }
    public string? ForeignCurrencyId { get; set; }
    public string? ForeignCurrencyCode { get; set; }

    public string? BudgetId { get; set; }
    public string? BudgetName { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? BillId { get; set; }
    public string? BillName { get; set; }

    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? InternalReference { get; set; }

    public string? InterestDate { get; set; }
    public string? BookDate { get; set; }
    public string? ProcessDate { get; set; }
    public string? DueDate { get; set; }
    public string? PaymentDate { get; set; }
    public string? InvoiceDate { get; set; }

    public string? SepaCc { get; set; }
    public string? SepaCtOp { get; set; }
    public string? SepaCtId { get; set; }
    public string? SepaDb { get; set; }
    public string? SepaCountry { get; set; }
    public string? SepaEp { get; set; }
    public string? SepaCi { get; set; }
    public string? SepaBatchId { get; set; }
}
