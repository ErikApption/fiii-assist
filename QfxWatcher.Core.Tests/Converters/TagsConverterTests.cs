using QfxWatcher.Services.Csv.Converter;

namespace QfxWatcher.Core.Tests.Converters;

public class TagsConverterTests
{
    [Fact]
    public void TagsComma_SplitsByComma()
    {
        var converter = new TagsCommaConverter();
        var result = converter.Convert("tag1, tag2, tag3");

        Assert.Equal(["tag1", "tag2", "tag3"], result);
    }

    [Fact]
    public void TagsComma_EmptyInput_ReturnsEmptyArray()
    {
        var converter = new TagsCommaConverter();
        var result = converter.Convert("");

        Assert.Empty(result);
    }

    [Fact]
    public void TagsComma_Null_ReturnsEmptyArray()
    {
        var converter = new TagsCommaConverter();
        var result = converter.Convert(null);

        Assert.Empty(result);
    }

    [Fact]
    public void TagsSpace_SplitsBySpace()
    {
        var converter = new TagsSpaceConverter();
        var result = converter.Convert("tag1 tag2 tag3");

        Assert.Equal(["tag1", "tag2", "tag3"], result);
    }

    [Fact]
    public void TagsSpace_MultipleSpaces_HandledCorrectly()
    {
        var converter = new TagsSpaceConverter();
        var result = converter.Convert("tag1   tag2");

        Assert.Equal(["tag1", "tag2"], result);
    }

    [Fact]
    public void TagsSpace_EmptyInput_ReturnsEmptyArray()
    {
        var converter = new TagsSpaceConverter();
        var result = converter.Convert("");

        Assert.Empty(result);
    }

    [Fact]
    public void TagsSpace_Null_ReturnsEmptyArray()
    {
        var converter = new TagsSpaceConverter();
        var result = converter.Convert(null);

        Assert.Empty(result);
    }
}
