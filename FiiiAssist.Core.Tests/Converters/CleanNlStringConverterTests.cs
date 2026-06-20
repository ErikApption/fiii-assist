using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class CleanNlStringConverterTests
{
    private readonly CleanNlStringConverter _converter = new();

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("line1\nline2", "line1\nline2")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Convert_PreservesNewlines(string? input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Fact]
    public void Convert_RemovesControlCharacters_KeepsNewlines()
    {
        var input = "hello\x01\nworld";
        Assert.Equal("hello\nworld", _converter.Convert(input));
    }
}
