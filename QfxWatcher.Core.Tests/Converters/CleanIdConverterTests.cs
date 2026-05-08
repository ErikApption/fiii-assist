using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class CleanIdConverterTests
{
    private readonly CleanIdConverter _converter = new();

    [Theory]
    [InlineData("42", 42)]
    [InlineData("1", 1)]
    [InlineData("-5", -5)]
    public void Convert_ValidId_ReturnsInteger(string input, int expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Convert_ZeroOrInvalid_ReturnsNull(string? input)
    {
        Assert.Null(_converter.Convert(input));
    }
}
