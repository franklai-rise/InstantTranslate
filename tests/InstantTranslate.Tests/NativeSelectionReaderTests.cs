using InstantTranslate.Selection;

namespace InstantTranslate.Tests;

public sealed class NativeSelectionReaderTests
{
    [Theory]
    [InlineData("Edit")]
    [InlineData("RichEdit20W")]
    [InlineData("RICHEDIT50W")]
    [InlineData("WindowsForms10.EDIT.app.0.1.abc")]
    public void RecognizesNativeEditControls(string className)
    {
        Assert.True(NativeSelectionReader.IsEditControl(className));
    }

    [Theory]
    [InlineData("Scintilla")]
    [InlineData("scintilla")]
    public void RecognizesScintillaControls(string className)
    {
        Assert.True(NativeSelectionReader.IsScintillaControl(className));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Chrome_RenderWidgetHostHWND")]
    public void RejectsUnsupportedControlClasses(string? className)
    {
        Assert.False(NativeSelectionReader.IsEditControl(className));
        Assert.False(NativeSelectionReader.IsScintillaControl(className));
    }
}
