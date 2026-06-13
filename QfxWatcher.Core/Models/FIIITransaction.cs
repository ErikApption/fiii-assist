namespace QfxWatcher.Models;

/// <summary>
/// A single financial transaction parsed from a QFX/OFX file.
/// </summary>
public class FIIITransaction
{
    /// <summary>Unique transaction identifier from the bank.</summary>
    public string FitId { get; set; } = string.Empty;

    /// <summary>Transaction date (YYYYMMDD in OFX; normalised here).</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Amount in decimal dollars.
    /// Negative values represent debits/spending; positive values represent credits.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>Payee / merchant name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional memo / description.</summary>
    public string Memo { get; set; } = string.Empty;

    /// <summary>OFX transaction type (DEBIT, CREDIT, XFER, etc.).</summary>
    public string TransactionType { get; set; } = string.Empty;

    /// <summary>
    /// Account number of the opposing account extracted from BANKACCTTO/BANKACCTFROM
    /// within the OFX transaction block. Used for transfer detection.
    /// </summary>
    public string OpposingAccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Bank ID (routing number) of the opposing account extracted from BANKACCTTO/BANKACCTFROM.
    /// </summary>
    public string OpposingBankId { get; set; } = string.Empty;

    /// <summary>
    /// Amount converted to integer milliunits (cents × 10) as required by Actual Budget.
    /// Actual Budget stores amounts as integers: $1.00 = 100, -$1.00 = -100.
    /// </summary>
    public int AmountMilliunits => (int)Math.Round(Amount * 100);
}
