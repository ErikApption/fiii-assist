using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class AmountNegatedConverterTests
{
    private readonly AmountNegatedConverter _converter = new();

    [Theory]
    [InlineData("12.34", -12.34)]
    [InlineData("-12.34", 12.34)]
    [InlineData("0", 0)]
    public void Convert_FlipsSign(string input, decimal expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Fact]
    public void Convert_Null_ReturnsZero()
    {
        Assert.Equal(0m, _converter.Convert(null));
    }
}
