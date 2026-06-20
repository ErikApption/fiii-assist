using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class AmountCreditConverterTests
{
    private readonly AmountCreditConverter _converter = new();

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    public void Convert_NullOrEmpty_ReturnsZero(string? input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("12.34", 12.34)]
    [InlineData("-12.34", 12.34)]
    [InlineData("0", 0)]
    public void Convert_AlwaysReturnsPositive(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }
}
