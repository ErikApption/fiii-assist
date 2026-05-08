namespace QfxWatcher.Services.Csv.Conversion;

/// <summary>
/// Converts rows of ColumnValues into PseudoTransaction objects.
/// </summary>
public sealed class ColumnValueConverter
{
    private readonly CsvImportConfiguration _config;

    public ColumnValueConverter(CsvImportConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Process multiple lines of ColumnValues into PseudoTransactions.
    /// </summary>
    public List<PseudoTransaction> ProcessLines(List<List<ColumnValue>> lines)
    {
        var result = new List<PseudoTransaction>();
        foreach (var line in lines)
            result.Add(ProcessLine(line));
        return result;
    }

    /// <summary>
    /// Process a single line of ColumnValues into a PseudoTransaction.
    /// </summary>
    public PseudoTransaction ProcessLine(List<ColumnValue> line)
    {
        var transaction = new PseudoTransaction
        {
            ErrorIfDuplicateHash = _config.IgnoreDuplicateTransactions,
            ApplyRules = _config.ApplyRules,
            FireWebhooks = _config.FireWebhooks,
        };

        foreach (var columnValue in line)
        {
            var role = columnValue.Role;
            var transactionField = RoleDefinitions.GetTransactionField(role);

            if (transactionField is null)
                continue;

            var parsedValue = columnValue.GetParsedValue();
            if (parsedValue is null)
                continue;

            transaction.SetField(transactionField, parsedValue, columnValue.AppendValue);
        }

        return transaction;
    }
}
