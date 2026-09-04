using InstantTranslate.Selection;

namespace InstantTranslate.Tests;

public sealed class EdgeAccessibilityActivatorTests
{
    [Theory]
    [InlineData("msedge")]
    [InlineData("MSEdge")]
    public void RecognizesOnlyMicrosoftEdge(string processName)
    {
        Assert.True(EdgeAccessibilityActivator.ShouldActivateProcessName(processName));
        Assert.False(EdgeAccessibilityActivator.ShouldActivateProcessName("chrome"));
        Assert.False(EdgeAccessibilityActivator.ShouldActivateProcessName("zotero"));
        Assert.False(EdgeAccessibilityActivator.ShouldActivateProcessName(null));
    }
}
