using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class BankDebitCreditConverterTests
{
    private readonly BankDebitCreditConverter _converter = new();

    [Theory]
    [InlineData("D", -1)]
    [InlineData("d", -1)]
    [InlineData("A", -1)]
    [InlineData("DR", -1)]
    [InlineData("af", -1)]
    [InlineData("DB", -1)]
    [InlineData("debet", -1)]
    [InlineData("DEBIT", -1)]
    [InlineData("S", -1)]
    [InlineData("dbit", -1)]
    [InlineData("charge", -1)]
    [InlineData("(-)", -1)]
    [InlineData("out", -1)]
    public void Convert_DebitIndicators_ReturnsNegativeOne(string input, int expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("C", 1)]
    [InlineData("credit", 1)]
    [InlineData("bij", 1)]
    [InlineData("", 1)]
    [InlineData("anything", 1)]
    public void Convert_CreditOrUnknown_ReturnsPositiveOne(string input, int expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Fact]
    public void Convert_Null_ReturnsPositiveOne()
    {
        Assert.Equal(1, _converter.Convert(null));
    }
}
