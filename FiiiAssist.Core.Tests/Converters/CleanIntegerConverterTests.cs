using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class CleanIntegerConverterTests
{
    private readonly CleanIntegerConverter _converter = new();

    [Theory]
    [InlineData("42", 42)]
    [InlineData("0", 0)]
    [InlineData("-10", -10)]
    public void Convert_ValidInteger_ReturnsValue(string input, int expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Convert_Invalid_ReturnsZero(string? input, int expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }
}
