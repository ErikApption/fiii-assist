using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class ConverterServiceTests
{
    [Fact]
    public void Exists_KnownConverter_ReturnsTrue()
    {
        Assert.True(ConverterService.Exists("AmountConverter"));
        Assert.True(ConverterService.Exists("DateConverter"));
        Assert.True(ConverterService.Exists("IbanConverter"));
    }

    [Fact]
    public void Exists_CaseInsensitive()
    {
        Assert.True(ConverterService.Exists("amountconverter"));
        Assert.True(ConverterService.Exists("AMOUNTCONVERTER"));
    }

    [Fact]
    public void Exists_UnknownConverter_ReturnsFalse()
    {
        Assert.False(ConverterService.Exists("NonExistentConverter"));
    }

    [Fact]
    public void Convert_ByName_InvokesCorrectConverter()
    {
        var result = ConverterService.Convert("AmountConverter", "12.34");
        Assert.Equal(12.34m, result);
    }

    [Fact]
    public void Convert_WithConfiguration_PassesConfig()
    {
        var result = ConverterService.Convert("DateConverter", "2024-03-15", "Y-m-d");
        Assert.IsType<DateTime>(result);
        Assert.Equal(new DateTime(2024, 3, 15), result);
    }

    [Fact]
    public void Convert_UnknownConverter_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConverterService.Convert("FakeConverter", "value"));
    }

    [Fact]
    public void Convert_EmptyClassName_ReturnsValueAsIs()
    {
        var result = ConverterService.Convert("", "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Convert_TypedOverload_WorksCorrectly()
    {
        var converter = new AmountConverter();
        var result = ConverterService.Convert(converter, "12.34");
        Assert.Equal(12.34m, result);
    }
}
