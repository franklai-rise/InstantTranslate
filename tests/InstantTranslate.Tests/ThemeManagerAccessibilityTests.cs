using System.Windows;
using InstantTranslate.Settings;
using WpfSystemColors = System.Windows.SystemColors;

namespace InstantTranslate.Tests;

public sealed class ThemeManagerAccessibilityTests
{
    [Fact]
    public void ApplyHighContrast_UsesSystemColorsForTextSelectionAndSurfaces()
    {
        var resources = new ResourceDictionary();

        ThemeManager.ApplyHighContrast(resources);

        Assert.Same(WpfSystemColors.WindowBrush, resources["PopupBackgroundBrush"]);
        Assert.Same(WpfSystemColors.WindowTextBrush, resources["PopupTextBrush"]);
        Assert.Same(WpfSystemColors.HighlightBrush, resources["AccentBrush"]);
        Assert.Same(WpfSystemColors.HighlightTextBrush, resources["AccentTextBrush"]);
        Assert.Same(WpfSystemColors.WindowBrush, resources["AppWindowBackgroundBrush"]);
        Assert.Same(WpfSystemColors.WindowTextBrush, resources["AppTextBrush"]);
    }
}
