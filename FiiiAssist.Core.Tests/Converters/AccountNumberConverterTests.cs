using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class AccountNumberConverterTests
{
    private readonly AccountNumberConverter _converter = new();

    [Theory]
    [InlineData("1234567890", "1234567890")]
    [InlineData("1234 5678 90", "1234567890")]
    [InlineData("  1234  ", "1234")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Convert_RemovesSpaces(string? input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }
}
