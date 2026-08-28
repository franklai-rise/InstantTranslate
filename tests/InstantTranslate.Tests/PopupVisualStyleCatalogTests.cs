using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class PopupVisualStyleCatalogTests
{
    [Theory]
    [InlineData("bubble")]
    [InlineData("BUBBLE")]
    [InlineData(" bubble ")]
    public void Normalize_BubbleAliases_ReturnsBubble(string value)
    {
        Assert.Equal(PopupVisualStyleCatalog.BubbleStyleId, PopupVisualStyleCatalog.Normalize(value));
        Assert.True(PopupVisualStyleCatalog.IsBubble(value));
    }

    [Theory]
    [InlineData("bubble-v2")]
    [InlineData("BUBBLE-V2")]
    [InlineData(" bubble-v2 ")]
    public void Normalize_BubbleV2Aliases_ReturnsBubbleV2(string value)
    {
        Assert.Equal(PopupVisualStyleCatalog.BubbleV2StyleId, PopupVisualStyleCatalog.Normalize(value));
        Assert.True(PopupVisualStyleCatalog.IsBubble(value));
        Assert.True(PopupVisualStyleCatalog.IsBubbleV2(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("minimal")]
    [InlineData("unknown")]
    public void Normalize_AnythingElse_ReturnsMinimal(string? value)
    {
        Assert.Equal(PopupVisualStyleCatalog.MinimalStyleId, PopupVisualStyleCatalog.Normalize(value));
        Assert.False(PopupVisualStyleCatalog.IsBubble(value));
    }
}
