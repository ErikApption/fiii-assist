using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class CleanUrlConverterTests
{
    private readonly CleanUrlConverter _converter = new();

    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("http://example.com/path", "http://example.com/path")]
    public void Convert_ValidUrl_ReturnsUrl(string input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Convert_InvalidUrl_ReturnsNull(string? input)
    {
        Assert.Null(_converter.Convert(input));
    }
}
