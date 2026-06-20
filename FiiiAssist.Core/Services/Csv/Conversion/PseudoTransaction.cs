using System.Globalization;

namespace FiiiAssist.Services.Csv.Conversion;

/// <summary>
/// Represents a pseudo-transaction built from CSV column values.
/// This is an intermediate representation before final submission to Firefly III.
/// </summary>
public sealed class PseudoTransaction
{
    // Transaction metadata
    public bool ErrorIfDuplicateHash { get; set; } = true;
    public bool ApplyRules { get; set; } = true;
    public bool FireWebhooks { get; set; } = true;

    // Core fields
    public string Type { get; set; } = "withdrawal";
    public string? Date { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }

    // Amount fields (intermediate, resolved during processing)
    public decimal? Amount { get; set; }
    public decimal? AmountDebit { get; set; }
    public decimal? AmountCredit { get; set; }
    public decimal? AmountNegated { get; set; }
    public int AmountModifier { get; set; } = 1;
    public decimal? ForeignAmount { get; set; }

    // Source account
    public int? SourceId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceIban { get; set; }
    public string? SourceNumber { get; set; }
    public string? SourceBic { get; set; }

    // Destination account
    public int? DestinationId { get; set; }
    public string? DestinationName { get; set; }
    public string? DestinationIban { get; set; }
    public string? DestinationNumber { get; set; }
    public string? DestinationBic { get; set; }

    // Currency
    public int? CurrencyId { get; set; }
    public string? CurrencyName { get; set; }
    public string? CurrencyCode { get; set; }
    public string? CurrencySymbol { get; set; }
    public int? ForeignCurrencyId { get; set; }
    public string? ForeignCurrencyCode { get; set; }

    // Categorization
    public int? BillId { get; set; }
    public string? BillName { get; set; }
    public int? BudgetId { get; set; }
    public string? BudgetName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }

    // Tags
    public List<string> TagsComma { get; set; } = [];
    public List<string> TagsSpace { get; set; } = [];
    public List<string> Tags { get; set; } = [];

    // Dates
    public string? InterestDate { get; set; }
    public string? BookDate { get; set; }
    public string? ProcessDate { get; set; }
    public string? DueDate { get; set; }
    public string? PaymentDate { get; set; }
    public string? InvoiceDate { get; set; }

    // External references
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? InternalReference { get; set; }
    public string? OriginalSource { get; set; }

    // SEPA fields
    public string? SepaCc { get; set; }
    public string? SepaCtOp { get; set; }
    public string? SepaCtId { get; set; }
    public string? SepaDb { get; set; }
    public string? SepaCountry { get; set; }
    public string? SepaEp { get; set; }
    public string? SepaCi { get; set; }
    public string? SepaBatchId { get; set; }

    /// <summary>
    /// Sets a field by its transaction field name (from RoleDefinitions).
    /// </summary>
    public void SetField(string fieldName, object? value, bool append)
    {
        if (value is null)
            return;

        switch (fieldName)
        {
            case "description":
                Description = append ? AppendString(Description, value) : value.ToString();
                break;
            case "notes":
                Notes = append ? AppendString(Notes, value) : value.ToString();
                break;
            case "date":
                Date = FormatDateValue(value);
                break;
            case "interest_date":
                InterestDate = FormatDateValue(value);
                break;
            case "book_date":
                BookDate = FormatDateValue(value);
                break;
            case "process_date":
                ProcessDate = FormatDateValue(value);
                break;
            case "due_date":
                DueDate = FormatDateValue(value);
                break;
            case "payment_date":
                PaymentDate = FormatDateValue(value);
                break;
            case "invoice_date":
                InvoiceDate = FormatDateValue(value);
                break;
            case "amount":
                Amount = ToDecimal(value);
                break;
            case "amount_debit":
                AmountDebit = ToDecimal(value);
                break;
            case "amount_credit":
                AmountCredit = ToDecimal(value);
                break;
            case "amount_negated":
                AmountNegated = ToDecimal(value);
                break;
            case "amount_modifier":
                AmountModifier = value is int i ? i : 1;
                break;
            case "foreign_amount":
                ForeignAmount = ToDecimal(value);
                break;
            case "source_id":
                SourceId = value is int sid ? sid : null;
                break;
            case "source_name":
                SourceName = value.ToString();
                break;
            case "source_iban":
                SourceIban = value.ToString();
                break;
            case "source_number":
                SourceNumber = value.ToString();
                break;
            case "source_bic":
                SourceBic = value.ToString();
                break;
            case "destination_id":
                DestinationId = value is int did ? did : null;
                break;
            case "destination_name":
                DestinationName = value.ToString();
                break;
            case "destination_iban":
                DestinationIban = value.ToString();
                break;
            case "destination_number":
                DestinationNumber = value.ToString();
                break;
            case "destination_bic":
                DestinationBic = value.ToString();
                break;
            case "currency_id":
                CurrencyId = value is int cid ? cid : null;
                break;
            case "currency_name":
                CurrencyName = value.ToString();
                break;
            case "currency_code":
                CurrencyCode = value.ToString();
                break;
            case "currency_symbol":
                CurrencySymbol = value.ToString();
                break;
            case "foreign_currency_id":
                ForeignCurrencyId = value is int fcid ? fcid : null;
                break;
            case "foreign_currency_code":
                ForeignCurrencyCode = value.ToString();
                break;
            case "bill_id":
                BillId = value is int bid ? bid : null;
                break;
            case "bill_name":
                BillName = value.ToString();
                break;
            case "budget_id":
                BudgetId = value is int buid ? buid : null;
                break;
            case "budget_name":
                BudgetName = value.ToString();
                break;
            case "category_id":
                CategoryId = value is int catid ? catid : null;
                break;
            case "category_name":
                CategoryName = value.ToString();
                break;
            case "tags_comma":
                if (value is string[] commaArr)
                    TagsComma.AddRange(commaArr);
                break;
            case "tags_space":
                if (value is string[] spaceArr)
                    TagsSpace.AddRange(spaceArr);
                break;
            case "external_id":
                ExternalId = value.ToString();
                break;
            case "external_url":
                ExternalUrl = value.ToString();
                break;
            case "internal_reference":
                InternalReference = append ? AppendString(InternalReference, value) : value.ToString();
                break;
            case "original_source":
                OriginalSource = value.ToString();
                break;
            case "sepa_cc":
                SepaCc = value.ToString();
                break;
            case "sepa_ct_op":
                SepaCtOp = value.ToString();
                break;
            case "sepa_ct_id":
                SepaCtId = value.ToString();
                break;
            case "sepa_db":
                SepaDb = value.ToString();
                break;
            case "sepa_country":
                SepaCountry = value.ToString();
                break;
            case "sepa_ep":
                SepaEp = value.ToString();
                break;
            case "sepa_ci":
                SepaCi = value.ToString();
                break;
            case "sepa_batch_id":
                SepaBatchId = value.ToString();
                break;
        }
    }

    private static string AppendString(string? existing, object? value)
    {
        var str = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(existing))
            return str;
        return $"{existing} {str}".Trim();
    }

    private static string? FormatDateValue(object? value)
    {
        return value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            string s => s,
            _ => value?.ToString()
        };
    }

    private static decimal? ToDecimal(object? value)
    {
        return value switch
        {
            decimal d => d,
            int i => i,
            string s when decimal.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }
}
