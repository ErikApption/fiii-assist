using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Services.Csv.Conversion;

/// <summary>
/// Processes pseudo-transactions into finalized transactions ready for Firefly III submission.
/// Handles amount resolution, tag merging, type determination, and positive amount enforcement.
/// Equivalent to the PHP PseudoTransactionProcessor + Task pipeline.
/// </summary>
public sealed class TransactionProcessor
{
    private readonly string? _defaultAccountId;

    public TransactionProcessor(string? defaultAccountId = null)
    {
        _defaultAccountId = defaultAccountId;
    }

    /// <summary>
    /// Process all pseudo-transactions through the finalization pipeline.
    /// </summary>
    public List<PseudoTransaction> Process(List<PseudoTransaction> transactions)
    {
        for (var i = 0; i < transactions.Count; i++)
        {
            transactions[i] = ProcessSingle(transactions[i]);
        }
        return transactions;
    }

    private PseudoTransaction ProcessSingle(PseudoTransaction tx)
    {
        ResolveAmount(tx);
        MergeTags(tx);
        ResolveAccounts(tx);
        EnforcePositiveAmount(tx);
        EnsureDescription(tx);
        return tx;
    }

    /// <summary>
    /// Resolves the final amount from amount/amount_debit/amount_credit/amount_negated fields,
    /// applies the amount_modifier, and determines transaction type.
    /// </summary>
    private static void ResolveAmount(PseudoTransaction tx)
    {
        decimal? amount = null;

        if (IsValidAmount(tx.Amount))
            amount = tx.Amount;

        if (amount is null && IsValidAmount(tx.AmountDebit))
            amount = tx.AmountDebit;

        if (amount is null && IsValidAmount(tx.AmountCredit))
            amount = tx.AmountCredit;

        if (amount is null && IsValidAmount(tx.AmountNegated))
            amount = tx.AmountNegated;

        if (amount is null || amount == 0m)
        {
            tx.Amount = 0m;
            return;
        }

        // Apply amount modifier (from debit/credit indicator columns)
        amount *= tx.AmountModifier;

        // Apply modifier to foreign amount too
        if (tx.ForeignAmount.HasValue && tx.ForeignAmount != 0m)
            tx.ForeignAmount *= tx.AmountModifier;

        tx.Amount = amount;

        // Determine type based on sign
        if (amount < 0m)
            tx.Type = "withdrawal";
        else if (amount > 0m)
            tx.Type = "deposit";

        // Clear intermediate fields
        tx.AmountDebit = null;
        tx.AmountCredit = null;
        tx.AmountNegated = null;
        tx.AmountModifier = 1;
    }

    /// <summary>
    /// Merges tags_comma and tags_space into a single deduplicated tags list.
    /// </summary>
    private static void MergeTags(PseudoTransaction tx)
    {
        var allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tx.TagsComma)
            if (!string.IsNullOrWhiteSpace(tag))
                allTags.Add(tag);

        foreach (var tag in tx.TagsSpace)
            if (!string.IsNullOrWhiteSpace(tag))
                allTags.Add(tag);

        tx.Tags = [.. allTags];
        tx.TagsComma.Clear();
        tx.TagsSpace.Clear();
    }

    /// <summary>
    /// Resolves source/destination accounts. If amount is positive (deposit),
    /// swaps source and destination. Falls back to default account for source.
    /// </summary>
    private void ResolveAccounts(PseudoTransaction tx)
    {
        var amount = tx.Amount ?? 0m;

        // If this is a withdrawal and amount is positive, swap source/destination
        if (tx.Type == "withdrawal" && amount > 0m)
        {
            SwapAccounts(tx);
            tx.Type = "deposit";
        }

        // If source has no ID and no name, fall back to default account
        if (tx.SourceId is null && string.IsNullOrWhiteSpace(tx.SourceName)
            && string.IsNullOrWhiteSpace(tx.SourceIban) && !string.IsNullOrWhiteSpace(_defaultAccountId))
        {
            if (int.TryParse(_defaultAccountId, out var defaultId))
                tx.SourceId = defaultId;
        }

        // If source ID is set, clear name/iban/number to let Firefly III resolve
        if (tx.SourceId is not null and not 0)
        {
            tx.SourceName = null;
            tx.SourceIban = null;
            tx.SourceNumber = null;
        }

        // Same for destination
        if (tx.DestinationId is not null and not 0)
        {
            tx.DestinationName = null;
            tx.DestinationIban = null;
            tx.DestinationNumber = null;
        }
    }

    /// <summary>
    /// Ensures the amount is always positive for Firefly III submission.
    /// The transaction type (withdrawal/deposit) already encodes the direction.
    /// </summary>
    private static void EnforcePositiveAmount(PseudoTransaction tx)
    {
        if (tx.Amount.HasValue)
            tx.Amount = AmountConverter.Positive(tx.Amount.Value);

        if (tx.ForeignAmount.HasValue && tx.ForeignAmount != 0m)
            tx.ForeignAmount = AmountConverter.Positive(tx.ForeignAmount.Value);
    }

    /// <summary>
    /// Ensures the transaction has a description. Falls back to "(no description)".
    /// </summary>
    private static void EnsureDescription(PseudoTransaction tx)
    {
        if (string.IsNullOrWhiteSpace(tx.Description))
            tx.Description = "(no description)";
    }

    private static void SwapAccounts(PseudoTransaction tx)
    {
        (tx.SourceId, tx.DestinationId) = (tx.DestinationId, tx.SourceId);
        (tx.SourceName, tx.DestinationName) = (tx.DestinationName, tx.SourceName);
        (tx.SourceIban, tx.DestinationIban) = (tx.DestinationIban, tx.SourceIban);
        (tx.SourceNumber, tx.DestinationNumber) = (tx.DestinationNumber, tx.SourceNumber);
        (tx.SourceBic, tx.DestinationBic) = (tx.DestinationBic, tx.SourceBic);
    }

    private static bool IsValidAmount(decimal? amount)
        => amount.HasValue && amount.Value != 0m;
}
