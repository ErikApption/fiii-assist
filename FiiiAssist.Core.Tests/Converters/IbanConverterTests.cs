using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class IbanConverterTests
{
    private readonly IbanConverter _converter = new();

    [Theory]
    [InlineData("GB29 NWBK 6016 1331 9268 19", "GB29NWBK60161331926819")]
    [InlineData("DE89370400440532013000", "DE89370400440532013000")]
    [InlineData("NL91ABNA0417164300", "NL91ABNA0417164300")]
    public void Convert_ValidIban_ReturnsNormalized(string input, string expected)
    {
        Assert.Equal(expected, _converter.Convert(input));
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("XX00INVALID")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    public void Convert_InvalidIban_ReturnsEmpty(string? input)
    {
        Assert.Equal("", _converter.Convert(input));
    }

    [Theory]
    [InlineData("GB29NWBK60161331926819", true)]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("NL91ABNA0417164300", true)]
    [InlineData("GB29NWBK60161331926818", false)] // wrong check digit
    [InlineData("", false)]
    [InlineData("ABC", false)]
    public void IsValidIban_ValidatesCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, IbanConverter.IsValidIban(input));
    }
}
