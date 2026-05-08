namespace QfxWatcher.Services.Csv.Conversion;

/// <summary>
/// Defines the mapping between CSV column roles, their converters, transaction fields, and behavior.
/// Equivalent to the PHP config/csv.php "import_roles" and "role_to_transaction" arrays.
/// </summary>
public static class RoleDefinitions
{
    /// <summary>
    /// Maps a role name to its converter class name (without "Converter" suffix).
    /// </summary>
    private static readonly Dictionary<string, RoleConfig> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["_ignore"] = new("Ignore", null, false),
        ["bill-id"] = new("CleanId", "bill_id", false),
        ["bill-name"] = new("CleanString", "bill_name", false),
        ["note"] = new("CleanNlString", "notes", true),
        ["currency-id"] = new("CleanId", "currency_id", false),
        ["currency-name"] = new("CleanString", "currency_name", false),
        ["currency-code"] = new("CleanString", "currency_code", false),
        ["currency-symbol"] = new("CleanString", "currency_symbol", false),
        ["foreign-currency-id"] = new("CleanId", "foreign_currency_id", false),
        ["foreign-currency-code"] = new("CleanString", "foreign_currency_code", false),
        ["external-id"] = new("CleanString", "external_id", false),
        ["external-url"] = new("CleanUrl", "external_url", false),
        ["description"] = new("CleanString", "description", true),
        ["date_transaction"] = new("Date", "date", false),
        ["date_interest"] = new("Date", "interest_date", false),
        ["date_book"] = new("Date", "book_date", false),
        ["date_process"] = new("Date", "process_date", false),
        ["date_due"] = new("Date", "due_date", false),
        ["date_payment"] = new("Date", "payment_date", false),
        ["date_invoice"] = new("Date", "invoice_date", false),
        ["budget-id"] = new("CleanId", "budget_id", false),
        ["budget-name"] = new("CleanString", "budget_name", false),
        ["rabo-debit-credit"] = new("BankDebitCredit", "amount_modifier", false),
        ["ing-debit-credit"] = new("BankDebitCredit", "amount_modifier", false),
        ["generic-debit-credit"] = new("BankDebitCredit", "amount_modifier", false),
        ["category-id"] = new("CleanId", "category_id", false),
        ["category-name"] = new("CleanString", "category_name", false),
        ["tags-comma"] = new("TagsComma", "tags_comma", true),
        ["tags-space"] = new("TagsSpace", "tags_space", true),
        ["account-id"] = new("CleanId", "source_id", false),
        ["account-name"] = new("CleanString", "source_name", false),
        ["account-iban"] = new("Iban", "source_iban", false),
        ["account-number"] = new("AccountNumber", "source_number", false),
        ["account-bic"] = new("AccountNumber", "source_bic", false),
        ["opposing-id"] = new("CleanId", "destination_id", false),
        ["opposing-name"] = new("CleanString", "destination_name", false),
        ["opposing-iban"] = new("Iban", "destination_iban", false),
        ["opposing-number"] = new("AccountNumber", "destination_number", false),
        ["opposing-bic"] = new("AccountNumber", "destination_bic", false),
        ["amount"] = new("Amount", "amount", false),
        ["amount_debit"] = new("AmountDebit", "amount_debit", false),
        ["amount_credit"] = new("AmountCredit", "amount_credit", false),
        ["amount_negated"] = new("AmountNegated", "amount_negated", false),
        ["amount_foreign"] = new("Amount", "foreign_amount", false),
        ["sepa_ct_id"] = new("Description", "sepa_ct_id", false),
        ["sepa_ct_op"] = new("Description", "sepa_ct_op", false),
        ["sepa_db"] = new("Description", "sepa_db", false),
        ["sepa_cc"] = new("Description", "sepa_cc", false),
        ["sepa_country"] = new("Description", "sepa_country", false),
        ["sepa_ep"] = new("Description", "sepa_ep", false),
        ["sepa_ci"] = new("Description", "sepa_ci", false),
        ["sepa_batch_id"] = new("Description", "sepa_batch_id", false),
        ["internal_reference"] = new("Description", "internal_reference", true),
        ["original-source"] = new("Description", "original_source", false),
    };

    /// <summary>
    /// When a column is mapped to an ID, the role may change (e.g. "account-name" → "account-id").
    /// </summary>
    private static readonly Dictionary<string, string> MappedRoleOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["account-id"] = "account-id",
        ["account-name"] = "account-id",
        ["account-iban"] = "account-id",
        ["account-number"] = "account-id",
        ["bill-id"] = "bill-id",
        ["bill-name"] = "bill-id",
        ["budget-id"] = "budget-id",
        ["budget-name"] = "budget-id",
        ["currency-id"] = "currency-id",
        ["currency-name"] = "currency-id",
        ["currency-code"] = "currency-id",
        ["currency-symbol"] = "currency-id",
        ["category-id"] = "category-id",
        ["category-name"] = "category-id",
        ["foreign-currency-id"] = "foreign-currency-id",
        ["foreign-currency-code"] = "foreign-currency-id",
        ["opposing-id"] = "opposing-id",
        ["opposing-name"] = "opposing-id",
        ["opposing-iban"] = "opposing-id",
        ["opposing-number"] = "opposing-id",
    };

    private static readonly HashSet<string> DateRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "date_transaction", "date_interest", "date_due", "date_payment", "date_process", "date_book", "date_invoice"
    };

    public static string? GetConverterName(string role)
        => Roles.TryGetValue(role, out var config) ? config.Converter : null;

    public static string? GetTransactionField(string role)
        => Roles.TryGetValue(role, out var config) ? config.TransactionField : null;

    public static bool IsAppendValue(string role)
        => Roles.TryGetValue(role, out var config) && config.AppendValue;

    public static bool IsDateRole(string role)
        => DateRoles.Contains(role);

    /// <summary>
    /// Gets the overridden role when a column value is mapped to an entity ID.
    /// </summary>
    public static string GetMappedRole(string originalRole, int mappedValue, bool doMapping)
    {
        if (mappedValue == 0)
            return originalRole;

        if (!doMapping)
            return originalRole;

        return MappedRoleOverrides.TryGetValue(originalRole, out var newRole) ? newRole : originalRole;
    }

    private sealed record RoleConfig(string Converter, string? TransactionField, bool AppendValue);
}
