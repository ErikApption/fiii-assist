using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class CleanStringConverterTests
{
    private readonly CleanStringConverter _converter = new();

    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("  hello  ", "hello")]
    [InlineData("line1\nline2", "line1 line2")]
    [InlineData("line1\r\nline2", "line1 line2")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Convert_CleansString(string? input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Fact]
    public void Convert_RemovesControlCharacters()
    {
        var input = "hello\x01\x02world";
        Assert.Equal("helloworld", _converter.Convert(input));
    }
}
