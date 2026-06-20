namespace FiiiAssist.Services.Csv.Conversion;

/// <summary>
/// Defines the mapping between CSV column roles, their converters, transaction fields, and behavior.
/// Equivalent to the PHP config/csv.php "import_roles" and "role_to_transaction" arrays.
/// </summary>
public static class RoleDefinitions
{
    /// <summary>
    /// Maps a role name to its converter class name.
    /// </summary>
    private static readonly Dictionary<string, RoleConfig> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["_ignore"] = new(null, null, false),
        ["bill-id"] = new("CleanIdConverter", "bill_id", false),
        ["bill-name"] = new("CleanStringConverter", "bill_name", false),
        ["note"] = new("CleanNlStringConverter", "notes", true),
        ["currency-id"] = new("CleanIdConverter", "currency_id", false),
        ["currency-name"] = new("CleanStringConverter", "currency_name", false),
        ["currency-code"] = new("CleanStringConverter", "currency_code", false),
        ["currency-symbol"] = new("CleanStringConverter", "currency_symbol", false),
        ["foreign-currency-id"] = new("CleanIdConverter", "foreign_currency_id", false),
        ["foreign-currency-code"] = new("CleanStringConverter", "foreign_currency_code", false),
        ["external-id"] = new("CleanStringConverter", "external_id", false),
        ["external-url"] = new("CleanUrlConverter", "external_url", false),
        ["description"] = new("CleanStringConverter", "description", true),
        ["date_transaction"] = new("DateConverter", "date", false),
        ["date_interest"] = new("DateConverter", "interest_date", false),
        ["date_book"] = new("DateConverter", "book_date", false),
        ["date_process"] = new("DateConverter", "process_date", false),
        ["date_due"] = new("DateConverter", "due_date", false),
        ["date_payment"] = new("DateConverter", "payment_date", false),
        ["date_invoice"] = new("DateConverter", "invoice_date", false),
        ["budget-id"] = new("CleanIdConverter", "budget_id", false),
        ["budget-name"] = new("CleanStringConverter", "budget_name", false),
        ["rabo-debit-credit"] = new("BankDebitCreditConverter", "amount_modifier", false),
        ["ing-debit-credit"] = new("BankDebitCreditConverter", "amount_modifier", false),
        ["generic-debit-credit"] = new("BankDebitCreditConverter", "amount_modifier", false),
        ["category-id"] = new("CleanIdConverter", "category_id", false),
        ["category-name"] = new("CleanStringConverter", "category_name", false),
        ["tags-comma"] = new("TagsCommaConverter", "tags_comma", true),
        ["tags-space"] = new("TagsSpaceConverter", "tags_space", true),
        ["account-id"] = new("CleanIdConverter", "source_id", false),
        ["account-name"] = new("CleanStringConverter", "source_name", false),
        ["account-iban"] = new("IbanConverter", "source_iban", false),
        ["account-number"] = new("AccountNumberConverter", "source_number", false),
        ["account-bic"] = new("AccountNumberConverter", "source_bic", false),
        ["opposing-id"] = new("CleanIdConverter", "destination_id", false),
        ["opposing-name"] = new("CleanStringConverter", "destination_name", false),
        ["opposing-iban"] = new("IbanConverter", "destination_iban", false),
        ["opposing-number"] = new("AccountNumberConverter", "destination_number", false),
        ["opposing-bic"] = new("AccountNumberConverter", "destination_bic", false),
        ["amount"] = new("AmountConverter", "amount", false),
        ["amount_debit"] = new("AmountDebitConverter", "amount_debit", false),
        ["amount_credit"] = new("AmountCreditConverter", "amount_credit", false),
        ["amount_negated"] = new("AmountNegatedConverter", "amount_negated", false),
        ["amount_foreign"] = new("AmountConverter", "foreign_amount", false),
        ["sepa_ct_id"] = new("DescriptionConverter", "sepa_ct_id", false),
        ["sepa_ct_op"] = new("DescriptionConverter", "sepa_ct_op", false),
        ["sepa_db"] = new("DescriptionConverter", "sepa_db", false),
        ["sepa_cc"] = new("DescriptionConverter", "sepa_cc", false),
        ["sepa_country"] = new("DescriptionConverter", "sepa_country", false),
        ["sepa_ep"] = new("DescriptionConverter", "sepa_ep", false),
        ["sepa_ci"] = new("DescriptionConverter", "sepa_ci", false),
        ["sepa_batch_id"] = new("DescriptionConverter", "sepa_batch_id", false),
        ["internal_reference"] = new("DescriptionConverter", "internal_reference", true),
        ["original-source"] = new("DescriptionConverter", "original_source", false),
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

    private sealed record RoleConfig(string? Converter, string? TransactionField, bool AppendValue);
}
