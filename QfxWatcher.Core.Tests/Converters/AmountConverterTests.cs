using System.Globalization;
using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class AmountConverterTests
{
    private readonly AmountConverter _converter = new();

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Convert_NullOrEmpty_ReturnsZero(string? input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("12.34", 12.34)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1 234.56", 1234.56)]
    [InlineData("0.5", 0.5)]
    [InlineData(".5", 0.5)]
    [InlineData("100", 100)]
    [InlineData("1000", 1000)]
    public void Convert_StandardFormats_ParsesCorrectly(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("-12.34", -12.34)]
    [InlineData("12.34-", 12.34)]   // trailing dash stripped, positive
    [InlineData("--12.34", 12.34)]  // leading -- stripped per PHP behavior
    [InlineData("(123.45)", -123.45)]
    public void Convert_NegativeFormats_ParsesCorrectly(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("€12.34", 12.34)]
    [InlineData("EUR 12.34", 12.34)]
    [InlineData("1.000,00 €", 1000.00)]
    public void Convert_CurrencySymbols_StrippedCorrectly(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("0,5", 0.5)]
    [InlineData("0,12345", 0.12345)]
    public void Convert_CommaDecimal_ParsesCorrectly(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Fact]
    public void Positive_NegativeValue_ReturnsPositive()
    {
        Assert.Equal(5.00m, AmountConverter.Positive(-5.00m));
    }

    [Fact]
    public void Positive_PositiveValue_ReturnsSame()
    {
        Assert.Equal(5.00m, AmountConverter.Positive(5.00m));
    }

    [Fact]
    public void Negative_PositiveValue_ReturnsNegative()
    {
        Assert.Equal(-5.00m, AmountConverter.Negative(5.00m));
    }

    [Fact]
    public void Negative_NegativeValue_ReturnsSame()
    {
        Assert.Equal(-5.00m, AmountConverter.Negative(-5.00m));
    }

    [Fact]
    public void Convert_FallbackLocale_UsesLocaleDecimalSeparator()
    {
        var previousLocale = AmountConverter.FallbackLocale;
        try
        {
            AmountConverter.FallbackLocale = new CultureInfo("de-DE"); // comma decimal
            // "14.000" with German locale: dot is thousands sep, so this is 14000
            var result = _converter.Convert("14.000");
            Assert.Equal(14000m, result);
        }
        finally
        {
            AmountConverter.FallbackLocale = previousLocale;
        }
    }
}
