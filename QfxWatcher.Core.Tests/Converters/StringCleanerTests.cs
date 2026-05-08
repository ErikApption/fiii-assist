using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class StringCleanerTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CleanString_BasicInput_ReturnsExpected(string? input, string expected)
    {
        Assert.Equal(expected, StringCleaner.CleanString(input ?? ""));
    }

    [Fact]
    public void CleanString_PreservesNewlines()
    {
        Assert.Equal("line1\nline2", StringCleaner.CleanString("line1\nline2"));
    }

    [Fact]
    public void CleanString_RemovesControlChars()
    {
        Assert.Equal("helloworld", StringCleaner.CleanString("hello\x01\x02world"));
    }

    [Fact]
    public void CleanString_PreservesTabs()
    {
        Assert.Equal("col1\tcol2", StringCleaner.CleanString("col1\tcol2"));
    }

    [Fact]
    public void CleanStringAndNewlines_ReplacesNewlinesWithSpace()
    {
        Assert.Equal("line1 line2", StringCleaner.CleanStringAndNewlines("line1\nline2"));
    }

    [Fact]
    public void CleanStringAndNewlines_RemovesControlChars()
    {
        Assert.Equal("helloworld", StringCleaner.CleanStringAndNewlines("hello\x01world"));
    }

    [Fact]
    public void CleanStringAndNewlines_TrimsResult()
    {
        Assert.Equal("hello", StringCleaner.CleanStringAndNewlines("  hello  "));
    }
}
