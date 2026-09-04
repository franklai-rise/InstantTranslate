using System.Text;

namespace InstantTranslate.Windows;

internal readonly record struct PopupAutoSize(double Width, double Height);

internal static class PopupAutoSizeCalculator
{
    internal const double MinimumWidth = 380;
    internal const double MinimumHeight = 192;
    internal const double PreferredMaximumWidth = 820;
    internal const double PreferredMaximumHeight = 600;

    private const double HorizontalChrome = 32;
    // Includes the single-row external controls, surface padding, and the
    // compact Q&A composer shown after translation.
    private const double VerticalChrome = 140;

    internal static PopupAutoSize Calculate(
        string? text,
        double fontSize,
        double availableWidth,
        double availableHeight,
        double actionBarWidth = 0)
    {
        var safeFontSize = double.IsFinite(fontSize) && fontSize > 0 ? fontSize : 16.5;
        var safeAvailableWidth = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : PreferredMaximumWidth;
        var safeAvailableHeight = double.IsFinite(availableHeight) && availableHeight > 0
            ? availableHeight
            : PreferredMaximumHeight;

        var maximumWidth = Math.Max(
            240,
            Math.Min(PreferredMaximumWidth, safeAvailableWidth));
        var maximumHeight = Math.Max(
            96,
            Math.Min(PreferredMaximumHeight, safeAvailableHeight));
        var minimumWidth = Math.Min(MinimumWidth, maximumWidth);
        var minimumHeight = Math.Min(MinimumHeight, maximumHeight);

        var lines = MeasureLineUnits(text);
        var totalUnits = lines.Sum();
        var longestLineUnits = lines.Max();
        var layoutUnits = Math.Max(totalUnits, longestLineUnits);
        var fontWidthScale = Math.Sqrt(safeFontSize / 16.5);
        var estimatedContentWidth = 260 + (Math.Sqrt(Math.Max(1, layoutUnits)) * 26 * fontWidthScale);
        var estimatedWindowWidth = estimatedContentWidth + HorizontalChrome;
        var desiredWidth = Math.Clamp(
            Math.Max(estimatedWindowWidth, actionBarWidth),
            minimumWidth,
            maximumWidth);

        var contentWidth = Math.Max(160, desiredWidth - HorizontalChrome);
        var unitsPerLine = Math.Max(8, contentWidth / (safeFontSize * 0.52));
        var wrappedLineCount = lines.Sum(lineUnits => Math.Max(1, (int)Math.Ceiling(lineUnits / unitsPerLine)));
        var estimatedWindowHeight = VerticalChrome + (wrappedLineCount * safeFontSize * 1.45);
        var desiredHeight = Math.Clamp(estimatedWindowHeight, minimumHeight, maximumHeight);

        return new PopupAutoSize(
            Math.Round(desiredWidth, 1),
            Math.Round(desiredHeight, 1));
    }

    private static IReadOnlyList<double> MeasureLineUnits(string? text)
    {
        var lines = new List<double> { 0 };
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\r')
            {
                continue;
            }

            if (rune.Value == '\n')
            {
                lines.Add(0);
                continue;
            }

            lines[^1] += GetLayoutUnits(rune);
        }

        return lines;
    }

    private static double GetLayoutUnits(Rune rune)
    {
        if (Rune.IsWhiteSpace(rune))
        {
            return 0.5;
        }

        if (rune.Value <= 0x7f)
        {
            return Rune.IsLetterOrDigit(rune) ? 1 : 0.7;
        }

        return IsWideRune(rune.Value) ? 2 : 1.35;
    }

    private static bool IsWideRune(int value)
    {
        return value is >= 0x1100 and <= 0x11ff
            or >= 0x2e80 and <= 0xa4cf
            or >= 0xac00 and <= 0xd7af
            or >= 0xf900 and <= 0xfaff
            or >= 0xfe10 and <= 0xfe6f
            or >= 0xff00 and <= 0xffef
            or >= 0x1f300 and <= 0x1faff
            or >= 0x20000 and <= 0x3fffd;
    }
}
