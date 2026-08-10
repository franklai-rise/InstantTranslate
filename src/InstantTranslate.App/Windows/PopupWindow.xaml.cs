using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using InstantTranslate.Interop;
using InstantTranslate.Models;
using InstantTranslate.Translation;
using Forms = System.Windows.Forms;
using WpfClipboard = System.Windows.Clipboard;
using WpfButton = System.Windows.Controls.Button;

namespace InstantTranslate.Windows;

internal partial class PopupWindow : Window
{
    private IntPtr _windowHandle;
    private string _sourceText = string.Empty;
    private string _translatedText = string.Empty;
    private string _targetLanguage = LanguageDirectionResolver.Chinese;
    private ScreenPoint _anchorPoint;

    public PopupWindow(long requestId)
    {
        RequestId = requestId;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public long RequestId { get; }

    public bool IsPinned { get; private set; }

    public bool HasTranslation => !string.IsNullOrWhiteSpace(_translatedText);

    public string SourceText => _sourceText;

    public ScreenPoint AnchorPoint => _anchorPoint;

    public event Action<PopupWindow, bool>? PinStateChanged;

    public event Action<PopupWindow, string>? RetranslateRequested;

    public void ShowLoading(ScreenPoint anchorPoint)
    {
        _anchorPoint = anchorPoint;
        TranslationTextBlock.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = IsPinned ? Visibility.Visible : Visibility.Collapsed;
        DirectionButton.IsEnabled = false;
        CopySourceButton.IsEnabled = false;
        CopyTranslationButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        ShowAt(anchorPoint);
    }

    public void ShowTranslation(
        string sourceText,
        string translatedText,
        string targetLanguage,
        ScreenPoint anchorPoint)
    {
        _sourceText = sourceText;
        _translatedText = translatedText;
        _targetLanguage = targetLanguage;
        _anchorPoint = anchorPoint;

        TranslationTextBlock.Text = translatedText;
        DirectionButton.Content = LanguageDirectionResolver.GetOppositeTarget(targetLanguage) == LanguageDirectionResolver.English
            ? "译为英文"
            : "译为中文";
        LoadingPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Visible;
        DirectionButton.IsEnabled = true;
        CopySourceButton.IsEnabled = true;
        CopyTranslationButton.IsEnabled = true;
        TranslationTextBlock.Visibility = Visibility.Visible;
        ShowAt(anchorPoint);
    }

    public void RestoreTranslationAfterFailure()
    {
        if (!HasTranslation)
        {
            Close();
            return;
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Visible;
        DirectionButton.IsEnabled = true;
        CopySourceButton.IsEnabled = true;
        CopyTranslationButton.IsEnabled = true;
        TranslationTextBlock.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        base.OnClosed(e);
    }

    private void ShowAt(ScreenPoint anchorPoint)
    {
        var wasVisible = IsVisible;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        if (!IsPinned || !wasVisible)
        {
            PositionNear(anchorPoint);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(WindowProcedure);

        var currentStyles = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        var requiredStyles = NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new IntPtr(currentStyles | requiredStyles));
    }

    private IntPtr WindowProcedure(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate)
        {
            handled = true;
            return new IntPtr(NativeMethods.MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private async void CopySourceButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyWithFeedbackAsync(CopySourceButton, _sourceText, "复制原文");
    }

    private async void CopyTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyWithFeedbackAsync(CopyTranslationButton, _translatedText, "复制译文");
    }

    private void DirectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceText.Length == 0)
        {
            return;
        }

        var nextTarget = LanguageDirectionResolver.GetOppositeTarget(_targetLanguage);
        RetranslateRequested?.Invoke(this, nextTarget);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsPinned)
        {
            IsPinned = false;
            PinStateChanged?.Invoke(this, false);
            Close();
            return;
        }

        IsPinned = true;
        PinButton.Tag = "Pinned";
        PinButton.ToolTip = "取消置顶并关闭";
        PinButton.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
        PopupSurface.Cursor = System.Windows.Input.Cursors.SizeAll;
        PinStateChanged?.Invoke(this, true);
    }

    private void PopupSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPinned || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // The mouse button may have been released before WPF starts the move loop.
        }
    }

    private static async Task CopyWithFeedbackAsync(WpfButton button, string text, string defaultToolTip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            WpfClipboard.SetText(text);
            button.ToolTip = "已复制";
            button.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 101, 52));
        }
        catch (ExternalException)
        {
            button.ToolTip = "复制失败";
            button.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
        }

        await Task.Delay(800);
        button.ClearValue(BackgroundProperty);
        button.ToolTip = defaultToolTip;
    }

    private void PositionNear(ScreenPoint anchorPoint)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        }

        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(anchorPoint.X, anchorPoint.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * scaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * scaleY));
        var offsetX = (int)Math.Round(12 * scaleX);
        var offsetY = (int)Math.Round(12 * scaleY);

        var x = anchorPoint.X + offsetX;
        var y = anchorPoint.Y + offsetY;
        if (x + width > screen.WorkingArea.Right)
        {
            x = anchorPoint.X - width - offsetX;
        }

        if (y + height > screen.WorkingArea.Bottom)
        {
            y = anchorPoint.Y - height - offsetY;
        }

        x = Math.Clamp(x, screen.WorkingArea.Left, Math.Max(screen.WorkingArea.Left, screen.WorkingArea.Right - width));
        y = Math.Clamp(y, screen.WorkingArea.Top, Math.Max(screen.WorkingArea.Top, screen.WorkingArea.Bottom - height));

        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }
}
