using System.Windows;
using System.Windows.Media;

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

        var palette = ThemeCatalog.Resolve(settings.ColorTheme, settings.CustomAccentColor);
        SetBrush(application.Resources, "AccentBrush", palette.Accent);
        SetBrush(application.Resources, "AccentLightBrush", palette.AccentLight);
        SetBrush(application.Resources, "PopupBackgroundBrush", palette.PopupBackground);
        SetBrush(application.Resources, "PopupBorderBrush", palette.PopupBorder);
        SetBrush(application.Resources, "PopupButtonBrush", palette.PopupButton);
        SetBrush(application.Resources, "PopupButtonHoverBrush", palette.PopupButtonHover);
        SetBrush(application.Resources, "PopupButtonBorderBrush", palette.PopupButtonBorder);
        SetBrush(application.Resources, "PopupTextBrush", palette.PopupText);
        SetBrush(application.Resources, "PopupMutedBrush", palette.PopupMuted);
        SetBrush(application.Resources, "PopupBadgeBrush", palette.PopupBadge);
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
}
