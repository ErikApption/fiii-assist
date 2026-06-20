using FiiiAssist.Services.Csv.Converter;

namespace FiiiAssist.Core.Tests.Converters;

public class DateConverterTests
{
    [Fact]
    public void Convert_DefaultFormat_ParsesYearMonthDay()
    {
        var converter = new DateConverter();
        var result = converter.Convert("2024-03-15");

        Assert.Equal(new DateTime(2024, 3, 15), result);
    }

    [Fact]
    public void Convert_NullOrEmpty_ReturnsToday()
    {
        var converter = new DateConverter();
        var result = converter.Convert(null);

        Assert.Equal(DateTime.Today, result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsToday()
    {
        var converter = new DateConverter();
        var result = converter.Convert("");

        Assert.Equal(DateTime.Today, result);
    }

    [Fact]
    public void Convert_WithPhpFormat_ParsesCorrectly()
    {
        var converter = new DateConverter();
        converter.SetConfiguration("d/m/Y");
        var result = converter.Convert("15/03/2024");

        Assert.Equal(new DateTime(2024, 3, 15), result);
    }

    [Fact]
    public void Convert_WithLocaleAndFormat_ParsesCorrectly()
    {
        var converter = new DateConverter();
        converter.SetConfiguration("en:Y-m-d");
        var result = converter.Convert("2024-03-15");

        Assert.Equal(new DateTime(2024, 3, 15), result);
    }

    [Fact]
    public void Convert_YearBefore1984_SetsCurrentYear()
    {
        var converter = new DateConverter();
        converter.SetConfiguration("d/m/Y");
        var result = converter.Convert("15/03/1900");

        Assert.Equal(DateTime.Now.Year, result.Year);
        Assert.Equal(15, result.Day);
    }

    [Fact]
    public void Convert_InvalidDate_ReturnsToday()
    {
        var converter = new DateConverter();
        var result = converter.Convert("not-a-date");

        Assert.Equal(DateTime.Today, result);
    }

    [Theory]
    [InlineData("en:Y-m-d", "en", "Y-m-d")]
    [InlineData("de:d.m.Y", "de", "d.m.Y")]
    [InlineData("Y-m-d", "en", "Y-m-d")]
    [InlineData("d/m/Y", "en", "d/m/Y")]
    public void SplitLocaleFormat_ParsesCorrectly(string input, string expectedLocale, string expectedFormat)
    {
        var (locale, format) = DateConverter.SplitLocaleFormat(input);
        Assert.Equal(expectedLocale, locale);
        Assert.Equal(expectedFormat, format);
    }
}
