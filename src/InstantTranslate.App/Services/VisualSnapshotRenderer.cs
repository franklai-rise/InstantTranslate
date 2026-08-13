using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InstantTranslate.Services;

/// <summary>
/// Renders only an InstantTranslate window into a PNG for deterministic visual
/// review. It never captures the desktop or another application's pixels.
/// </summary>
internal static class VisualSnapshotRenderer
{
    internal static void SavePng(FrameworkElement element, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        element.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(element);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var directory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("预览图片路径无效。");
        Directory.CreateDirectory(directory);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }
}
