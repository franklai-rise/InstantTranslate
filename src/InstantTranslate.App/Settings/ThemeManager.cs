using System.Windows;
using System.Windows.Media;
using WpfSystemColors = System.Windows.SystemColors;

namespace InstantTranslate.Settings;

internal static class ThemeManager
{
    internal static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (System.Windows.Application.Current is not { } application)
        {
            return;
        }

        if (SystemParameters.HighContrast)
        {
            ApplyHighContrast(application.Resources);
            return;
        }

        var palette = ThemeCatalog.Resolve(settings.ColorTheme, settings.CustomAccentColor);
        SetBrush(application.Resources, "AppWindowBackgroundBrush", "#F5F5F7");
        SetBrush(application.Resources, "AppCardBackgroundBrush", "#FFFFFF");
        SetBrush(application.Resources, "AppCardBorderBrush", "#E5E5EA");
        SetBrush(application.Resources, "AppFieldBackgroundBrush", "#F2F2F7");
        SetBrush(application.Resources, "AppFieldBorderBrush", "#D1D1D6");
        SetBrush(application.Resources, "AppTextBrush", "#1D1D1F");
        SetBrush(application.Resources, "AppMutedTextBrush", "#6E6E73");
        SetBrush(application.Resources, "AppHoverBrush", "#E9E9ED");
        SetBrush(application.Resources, "AppPressedBrush", "#DEDEE3");
        SetBrush(application.Resources, "SuccessBrush", "#16784A");
        SetBrush(application.Resources, "DangerBrush", "#B42318");
        SetBrush(application.Resources, "AccentBrush", palette.Accent);
        SetBrush(application.Resources, "AccentLightBrush", palette.AccentLight);
        SetBrush(application.Resources, "AccentTextBrush", "#FFFFFF");
        SetBrush(application.Resources, "SwitchKnobBrush", "#FFFFFF");
        SetBrush(application.Resources, "PopupBackgroundBrush", palette.PopupBackground);
        SetBrush(application.Resources, "PopupBorderBrush", palette.PopupBorder);
        SetBrush(application.Resources, "PopupButtonBrush", palette.PopupButton);
        SetBrush(application.Resources, "PopupButtonHoverBrush", palette.PopupButtonHover);
        SetBrush(application.Resources, "PopupButtonBorderBrush", palette.PopupButtonBorder);
        SetBrush(application.Resources, "PopupTextBrush", palette.PopupText);
        SetBrush(application.Resources, "PopupMutedBrush", palette.PopupMuted);
        SetBrush(application.Resources, "PopupBadgeBrush", palette.PopupBadge);
    }

    internal static void ApplyHighContrast(ResourceDictionary resources)
    {
        SetBrush(resources, "AppWindowBackgroundBrush", WpfSystemColors.WindowBrush);
        SetBrush(resources, "AppCardBackgroundBrush", WpfSystemColors.WindowBrush);
        SetBrush(resources, "AppCardBorderBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "AppFieldBackgroundBrush", WpfSystemColors.WindowBrush);
        SetBrush(resources, "AppFieldBorderBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "AppTextBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "AppMutedTextBrush", WpfSystemColors.GrayTextBrush);
        SetBrush(resources, "AppHoverBrush", WpfSystemColors.HighlightBrush);
        SetBrush(resources, "AppPressedBrush", WpfSystemColors.HotTrackBrush);
        SetBrush(resources, "SuccessBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "DangerBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "AccentBrush", WpfSystemColors.HighlightBrush);
        SetBrush(resources, "AccentLightBrush", WpfSystemColors.HighlightBrush);
        SetBrush(resources, "AccentTextBrush", WpfSystemColors.HighlightTextBrush);
        SetBrush(resources, "SwitchKnobBrush", WpfSystemColors.HighlightTextBrush);
        SetBrush(resources, "PopupBackgroundBrush", WpfSystemColors.WindowBrush);
        SetBrush(resources, "PopupBorderBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "PopupButtonBrush", WpfSystemColors.WindowBrush);
        SetBrush(resources, "PopupButtonHoverBrush", WpfSystemColors.HighlightBrush);
        SetBrush(resources, "PopupButtonBorderBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "PopupTextBrush", WpfSystemColors.WindowTextBrush);
        SetBrush(resources, "PopupMutedBrush", WpfSystemColors.GrayTextBrush);
        SetBrush(resources, "PopupBadgeBrush", WpfSystemColors.HighlightBrush);
    }

    internal static SolidColorBrush CreateBrush(string colorValue)
    {
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorValue);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string colorValue)
    {
        resources[key] = CreateBrush(colorValue);
    }

    private static void SetBrush(ResourceDictionary resources, string key, System.Windows.Media.Brush brush)
    {
        resources[key] = brush;
    }
}
