using InstantTranslate.Selection;

namespace InstantTranslate.Tests;

public sealed class UiaSelectionReaderTests
{
    [Theory]
    [InlineData(42, 42, true)]
    [InlineData(42, 43, false)]
    [InlineData(0, 0, false)]
    [InlineData(null, 42, false)]
    [InlineData(42, null, false)]
    public void FocusedElementMustBelongToHitElementProcess(
        int? hitProcessId,
        int? focusedProcessId,
        bool expected)
    {
        Assert.Equal(
            expected,
            UiaSelectionReader.ShouldIncludeFocusedElement(hitProcessId, focusedProcessId));
    }
}
