using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class DescriptionConverterTests
{
    private readonly DescriptionConverter _converter = new();

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("  hello  ", "hello")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Convert_TrimsValue(string? input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }
}
