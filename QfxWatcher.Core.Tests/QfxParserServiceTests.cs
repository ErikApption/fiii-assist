using QfxWatcher.Services;

namespace QfxWatcher.Core.Tests;

public class QfxParserServiceTests
{
    [Fact]
    public void Parse_SgmlContent_ReturnsTransactions()
    {
        var content = """
            OFXHEADER:100
            DATA:OFXSGML
            <OFX>
              <BANKMSGSRSV1>
                <STMTTRNRS>
                  <STMTRS>
                    <BANKTRANLIST>
                      <STMTTRN>
                        <TRNTYPE>DEBIT
                        <DTPOSTED>20250501120000
                        <TRNAMT>-12.34
                        <FITID>abc-123
                        <NAME>Coffee Shop
                        <MEMO>Morning coffee
                      </STMTTRN>
                    </BANKTRANLIST>
                  </STMTRS>
                </STMTTRNRS>
              </BANKMSGSRSV1>
            </OFX>
            """;

        var result = QfxParserService.Parse(content);

        var transaction = Assert.Single(result);
        Assert.Equal("abc-123", transaction.FitId);
        Assert.Equal("DEBIT", transaction.TransactionType);
        Assert.Equal(new DateOnly(2025, 5, 1), transaction.Date);
        Assert.Equal(-12.34m, transaction.Amount);
        Assert.Equal("Coffee Shop", transaction.Name);
        Assert.Equal("Morning coffee", transaction.Memo);
    }

    [Fact]
    public void Parse_XmlContent_ReturnsTransactions()
    {
        var content = """
            <?xml version="1.0" encoding="UTF-8"?>
            <OFX>
              <BANKMSGSRSV1>
                <STMTTRNRS>
                  <STMTRS>
                    <BANKTRANLIST>
                      <STMTTRN>
                        <TRNTYPE>CREDIT</TRNTYPE>
                        <DTPOSTED>20250430100000</DTPOSTED>
                        <TRNAMT>100.00</TRNAMT>
                        <FITID>fit-987</FITID>
                        <NAME>Refund</NAME>
                        <MEMO>Store refund</MEMO>
                      </STMTTRN>
                    </BANKTRANLIST>
                  </STMTRS>
                </STMTTRNRS>
              </BANKMSGSRSV1>
            </OFX>
            """;

        var result = QfxParserService.Parse(content);

        var transaction = Assert.Single(result);
        Assert.Equal("fit-987", transaction.FitId);
        Assert.Equal("CREDIT", transaction.TransactionType);
        Assert.Equal(new DateOnly(2025, 4, 30), transaction.Date);
        Assert.Equal(100.00m, transaction.Amount);
        Assert.Equal("Refund", transaction.Name);
        Assert.Equal("Store refund", transaction.Memo);
    }
}
