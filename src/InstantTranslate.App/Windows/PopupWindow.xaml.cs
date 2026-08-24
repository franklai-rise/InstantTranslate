using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using InstantTranslate.Interop;
using InstantTranslate.Models;
using InstantTranslate.Translation;
using Forms = System.Windows.Forms;
using WpfClipboard = System.Windows.Clipboard;
using WpfButton = System.Windows.Controls.Button;

namespace InstantTranslate.Windows;

internal partial class PopupWindow : Window
{
    private const double DefaultTranslationFontSize = 16.5;
    private const string DefaultEnglishTranslationFontFamily = "Times New Roman";
    private const string DefaultChineseTranslationFontFamily = "SimHei, 黑体, Microsoft YaHei UI";
    private const int AutomaticSizeGrowthThreshold = 12;
    private System.Windows.Media.FontFamily _chineseTranslationFont;
    private System.Windows.Media.FontFamily _englishTranslationFont;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private HwndSource? _hwndSource;
    private IntPtr _windowHandle;
    private string _sourceText = string.Empty;
    private string _currentTranslationSourceText = string.Empty;
    private string _currentSourceLanguage = "自动检测";
    private string _translatedText = string.Empty;
    private CompletedTranslation? _lastCompletedTranslation;
    private string _translationBeforeEdit = string.Empty;
    private string _targetLanguage = LanguageDirectionResolver.Chinese;
    private ScreenPoint _anchorPoint;
    private bool _hasDisplayedTranslation;
    private Paragraph? _translationParagraph;
    private string _renderedTranslation = string.Empty;
    private int _copyFeedbackVersion;
    private bool _isRepositionPending;
    private string _uiLanguage = "en";
    private ActionMessageKind _actionMessageKind;
    private BodyMessageKind _bodyMessageKind = BodyMessageKind.Translating;
    private string? _customFailureMessage;
    private bool _isEditingTranslation;
    private bool _isAutomaticSizing;
    private int _lastAutomaticSizeTextLength;

    public PopupWindow(
        long requestId,
        double defaultFontSize = DefaultTranslationFontSize,
        string englishFontFamily = DefaultEnglishTranslationFontFamily,
        string chineseFontFamily = DefaultChineseTranslationFontFamily,
        string uiLanguage = "en")
    {
        RequestId = requestId;
        _englishTranslationFont = CreateFontFamily(englishFontFamily, DefaultEnglishTranslationFontFamily);
        _chineseTranslationFont = CreateFontFamily(chineseFontFamily, DefaultChineseTranslationFontFamily);
        InitializeComponent();
        TranslationRichTextBox.FontFamily = _englishTranslationFont;
        FontSizeSlider.Value = Math.Clamp(defaultFontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
        ApplyUiLanguage(uiLanguage);
        SourceInitialized += OnSourceInitialized;
    }

    public long RequestId { get; }

    public bool IsPinned { get; private set; }

    public bool HasTranslation => _lastCompletedTranslation is not null;

    public string SourceText => _sourceText;

    public string CurrentTranslationSourceText => _currentTranslationSourceText;

    public string CurrentSourceLanguage => _currentSourceLanguage;

    public string LanguageSwitchSourceText => _translatedText;

    public string CurrentTargetLanguage => _targetLanguage;

    public ScreenPoint AnchorPoint => _anchorPoint;

    public bool ContainsScreenPoint(ScreenPoint point)
    {
        return IsVisible
               && _windowHandle != IntPtr.Zero
               && NativeMethods.GetWindowRect(_windowHandle, out var rectangle)
               && rectangle.Contains(point);
    }

    public event Action<PopupWindow, bool>? PinStateChanged;

    public event Action<PopupWindow, string>? RetranslateRequested;

    public event Func<PopupWindow, string, bool>? CorrectionSaveRequested;

    public void ApplyUiLanguage(string? uiLanguage)
    {
        _uiLanguage = string.Equals(uiLanguage, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiLanguage, "zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en";

        FontSizeButton.ToolTip = Localize("Text size", "文字大小");
        FontSizePanel.ToolTip = Localize("Drag to adjust text size", "拖动调节字体大小");
        DirectionButton.ToolTip = Localize("Switch translation language", "切换翻译语言");
        CopySourceButton.Content = Localize("Source", "原文");
        CopyTranslationButton.Content = Localize("Translation", "译文");
        CopySourceButton.ToolTip = Localize("Copy source", "复制原文");
        CopyTranslationButton.ToolTip = Localize("Copy translation", "复制译文");
        EditTranslationButton.ToolTip = _isEditingTranslation
            ? Localize("Save correction (Ctrl+Enter)", "保存修正（Ctrl+Enter）")
            : Localize("Edit and save correction (Ctrl+E)", "编辑并保存修正（Ctrl+E）");
        PinButton.ToolTip = IsPinned
            ? Localize("Release window", "取消保留")
            : Localize("Keep window", "保留此窗口");
        CloseButton.ToolTip = Localize("Close", "关闭");
        CopySelectionMenuItem.Header = Localize("Copy", "复制");
        SelectAllMenuItem.Header = Localize("Select all", "全选");
        System.Windows.Automation.AutomationProperties.SetName(
            FontSizeButton,
            Localize("Text size", "文字大小"));
        System.Windows.Automation.AutomationProperties.SetName(
            DirectionButton,
            Localize("Switch translation language", "切换翻译语言"));
        System.Windows.Automation.AutomationProperties.SetName(
            CopySourceButton,
            Localize("Copy source", "复制原文"));
        System.Windows.Automation.AutomationProperties.SetName(
            CopyTranslationButton,
            Localize("Copy translation", "复制译文"));
        System.Windows.Automation.AutomationProperties.SetName(
            EditTranslationButton,
            _isEditingTranslation
                ? Localize("Save correction", "保存修正")
                : Localize("Edit translation", "编辑译文"));
        System.Windows.Automation.AutomationProperties.SetName(
            PinButton,
            IsPinned ? Localize("Release window", "取消保留") : Localize("Keep window", "保留此窗口"));
        System.Windows.Automation.AutomationProperties.SetName(
            CloseButton,
            Localize("Close", "关闭"));

        UpdateDirectionButtonText();
        UpdateBodyMessageText();
        UpdateActionMessageText();
    }

    public void ApplyAppearance(
        string? englishFontFamily,
        string? chineseFontFamily,
        string? uiLanguage)
    {
        _englishTranslationFont = CreateFontFamily(
            englishFontFamily,
            DefaultEnglishTranslationFontFamily);
        _chineseTranslationFont = CreateFontFamily(
            chineseFontFamily,
            DefaultChineseTranslationFontFamily);
        ApplyUiLanguage(uiLanguage);

        if (_translatedText.Length > 0)
        {
            _translationParagraph = null;
            _renderedTranslation = string.Empty;
            SetTranslationText(_translatedText);
        }
    }

    internal void SelectTextForVisualTest(int start, int length)
    {
        if (_translationParagraph is null || _renderedTranslation.Length == 0)
        {
            return;
        }

        var safeStart = Math.Clamp(start, 0, _renderedTranslation.Length);
        var safeLength = Math.Clamp(length, 0, _renderedTranslation.Length - safeStart);
        var selectionStart = GetTranslationPointerAtOffset(safeStart);
        var selectionEnd = GetTranslationPointerAtOffset(safeStart + safeLength);
        if (selectionStart is null || selectionEnd is null)
        {
            return;
        }

        Activate();
        TranslationRichTextBox.Focus();
        TranslationRichTextBox.Selection.Select(selectionStart, selectionEnd);
    }

    internal void ConfigureViewportForVisualTest(double width, double height, double fontSize)
    {
        EnterManualSizeMode(updateLayout: true);
        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
        FontSizeSlider.Value = Math.Clamp(fontSize, FontSizeSlider.Minimum, FontSizeSlider.Maximum);
        UpdateLayout();
    }

    internal bool HasActiveSelectionForVisualTest()
    {
        return IsActive
            && TranslationRichTextBox.IsKeyboardFocusWithin
            && !TranslationRichTextBox.Selection.IsEmpty
            && !string.IsNullOrWhiteSpace(TranslationRichTextBox.Selection.Text);
    }

    internal bool HasVerticalOverflowForVisualTest()
    {
        UpdateLayout();
        return TranslationRichTextBox.ExtentHeight > TranslationRichTextBox.ViewportHeight + 1;
    }

    public void ShowLoading(ScreenPoint anchorPoint)
    {
        CancelTranslationEditing(showStatus: false);
        if (_hasDisplayedTranslation)
        {
            PreserveCurrentWindowSize();
            LoadingPanel.Visibility = Visibility.Collapsed;
            TranslationRichTextBox.Visibility = Visibility.Visible;
            SetActionBarVisibility(Visibility.Visible);
            ShowActionStatus(ActionMessageKind.Retranslating);
        }
        else
        {
            TranslationRichTextBox.Visibility = Visibility.Collapsed;
            _bodyMessageKind = BodyMessageKind.Translating;
            _customFailureMessage = null;
            UpdateBodyMessageText();
            LoadingPanel.Visibility = Visibility.Collapsed;
            SetActionBarVisibility(Visibility.Collapsed);
        }

        _anchorPoint = anchorPoint;
        DirectionButton.IsEnabled = false;
        CopySourceButton.IsEnabled = false;
        CopyTranslationButton.IsEnabled = false;
        EditTranslationButton.IsEnabled = false;
        if (_hasDisplayedTranslation)
        {
            ShowAt(anchorPoint);
        }
    }

    public void ShowTranslation(
        string sourceText,
        string translatedText,
        string targetLanguage,
        ScreenPoint anchorPoint,
        string sourceLanguage = "自动检测")
    {
        if (string.IsNullOrEmpty(_sourceText))
        {
            _sourceText = sourceText;
        }
        _currentTranslationSourceText = sourceText;
        _currentSourceLanguage = sourceLanguage;
        _translatedText = translatedText;
        _targetLanguage = targetLanguage;
        _anchorPoint = anchorPoint;

        var isFirstTranslationUpdate = !_hasDisplayedTranslation;
        if (isFirstTranslationUpdate)
        {
            _isAutomaticSizing = true;
            _lastAutomaticSizeTextLength = 0;
        }

        SetTranslationText(translatedText);
        UpdateDirectionButtonText();
        LoadingPanel.Visibility = Visibility.Collapsed;
        SetActionBarVisibility(Visibility.Visible);
        DirectionButton.IsEnabled = false;
        CopySourceButton.IsEnabled = true;
        CopyTranslationButton.IsEnabled = true;
        EditTranslationButton.IsEnabled = false;
        TranslationRichTextBox.Visibility = Visibility.Visible;
        HideActionStatus();
        _hasDisplayedTranslation = true;
        ApplyAutomaticSize(translatedText, anchorPoint, force: isFirstTranslationUpdate);
        if (isFirstTranslationUpdate || !IsVisible)
        {
            ShowAt(anchorPoint);
        }
    }

    public void MarkTranslationComplete()
    {
        ApplyAutomaticSize(_translatedText, _anchorPoint, force: true);
        if (!string.IsNullOrWhiteSpace(_translatedText))
        {
            _lastCompletedTranslation = new CompletedTranslation(
                _currentTranslationSourceText,
                _currentSourceLanguage,
                _translatedText,
                _targetLanguage);
        }

        if (HasTranslation && !_isEditingTranslation)
        {
            DirectionButton.IsEnabled = true;
            CopySourceButton.IsEnabled = true;
            CopyTranslationButton.IsEnabled = true;
            EditTranslationButton.IsEnabled = true;
        }
    }

    public void RestoreTranslationAfterFailure()
    {
        if (_lastCompletedTranslation is not { } completed)
        {
            Close();
            return;
        }

        _currentTranslationSourceText = completed.SourceText;
        _currentSourceLanguage = completed.SourceLanguage;
        _translatedText = completed.TranslatedText;
        _targetLanguage = completed.TargetLanguage;
        SetTranslationText(_translatedText);
        UpdateDirectionButtonText();

        LoadingPanel.Visibility = Visibility.Collapsed;
        SetActionBarVisibility(Visibility.Visible);
        DirectionButton.IsEnabled = true;
        CopySourceButton.IsEnabled = true;
        CopyTranslationButton.IsEnabled = true;
        EditTranslationButton.IsEnabled = true;
        TranslationRichTextBox.Visibility = Visibility.Visible;
        ShowActionStatus(ActionMessageKind.TranslationFailedPreserved);
    }

    public void ShowFailure(string? message = null)
    {
        _customFailureMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        _bodyMessageKind = _customFailureMessage is null
            ? BodyMessageKind.Failure
            : BodyMessageKind.CustomFailure;
        UpdateBodyMessageText();
        TranslationRichTextBox.Visibility = Visibility.Collapsed;
        SetActionBarVisibility(Visibility.Visible);
        DirectionButton.IsEnabled = false;
        CopySourceButton.IsEnabled = false;
        CopyTranslationButton.IsEnabled = false;
        EditTranslationButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        ShowAt(_anchorPoint);
    }

    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        _lifetimeCancellation.Cancel();
        BeginAnimation(OpacityProperty, null);
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WindowProcedure);
            _hwndSource = null;
        }

        TranslationRichTextBox.Document.Blocks.Clear();
        _translationParagraph = null;
        _renderedTranslation = string.Empty;
        _sourceText = string.Empty;
        _currentTranslationSourceText = string.Empty;
        _currentSourceLanguage = "自动检测";
        _translatedText = string.Empty;
        _lastCompletedTranslation = null;
        _windowHandle = IntPtr.Zero;
        base.OnClosed(e);
        _lifetimeCancellation.Dispose();
    }

    private void ShowAt(ScreenPoint anchorPoint)
    {
        var wasVisible = IsVisible;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            if (SystemParameters.ClientAreaAnimation)
            {
                BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    });
            }
            else
            {
                Opacity = 1;
            }
        }

        UpdateLayout();
        if (!IsPinned || !wasVisible)
        {
            PositionNear(anchorPoint);
        }

        if (!wasVisible && !IsPinned)
        {
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (IsVisible && !IsPinned)
                    {
                        PositionNear(_anchorPoint);
                    }
                },
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_windowHandle);
        _hwndSource?.AddHook(WindowProcedure);

        var currentStyles = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle).ToInt64();
        var requiredStyles = (currentStyles | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new IntPtr(requiredStyles));
    }

    private IntPtr WindowProcedure(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmNcHitTest
            && IsVisible
            && NativeMethods.GetWindowRect(windowHandle, out var bounds))
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var edgeThickness = Math.Max(10, (int)Math.Ceiling(10 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY)));
            var hitTest = PopupResizeHitTest.ResolveWithInsetTop(
                bounds,
                GetPopupSurfaceTop(bounds),
                PopupResizeHitTest.DecodeScreenPoint(lParam),
                edgeThickness);
            if (hitTest != 0)
            {
                EnterManualSizeMode(updateLayout: false);
                handled = true;
                return new IntPtr(hitTest);
            }
        }

        return IntPtr.Zero;
    }

    private int GetPopupSurfaceTop(NativeMethods.NativeRect windowBounds)
    {
        if (PopupSurface.ActualHeight <= 0)
        {
            return windowBounds.Top;
        }

        try
        {
            var surfaceTopLeft = PopupSurface.PointToScreen(new System.Windows.Point(0, 0));
            return Math.Clamp(
                (int)Math.Round(surfaceTopLeft.Y),
                windowBounds.Top,
                windowBounds.Bottom - 1);
        }
        catch (InvalidOperationException)
        {
            return windowBounds.Top;
        }
    }

    private async void CopySourceButton_Click(object sender, RoutedEventArgs e)
    {
        await CopyWithFeedbackAsync(CopySourceButton, _sourceText);
    }

    private async void CopyTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = TranslationRichTextBox.Selection.Text;
        var textToCopy = string.IsNullOrEmpty(selectedText) ? _translatedText : selectedText;
        await CopyWithFeedbackAsync(CopyTranslationButton, textToCopy);
    }

    private void EditTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditingTranslation)
        {
            TrySaveTranslationCorrection();
            return;
        }

        BeginTranslationEditing();
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

    private void FontSizeButton_Click(object sender, RoutedEventArgs e)
    {
        var isExpanded = FontSizePanel.Visibility == Visibility.Visible;
        FontSizePanel.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        FontSizeButton.Tag = isExpanded ? null : "Selected";
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsPinned)
        {
            IsPinned = false;
            PinButton.Tag = null;
            PinButton.ToolTip = Localize("Keep window", "保留此窗口");
            System.Windows.Automation.AutomationProperties.SetName(
                PinButton,
                Localize("Keep window", "保留此窗口"));
            PopupSurface.Cursor = System.Windows.Input.Cursors.Arrow;
            PinStateChanged?.Invoke(this, false);
            return;
        }

        IsPinned = true;
        EnterManualSizeMode(updateLayout: true);
        PinButton.Tag = "Pinned";
        PinButton.ToolTip = Localize("Release window", "取消保留");
        System.Windows.Automation.AutomationProperties.SetName(
            PinButton,
            Localize("Release window", "取消保留"));
        PopupSurface.Cursor = System.Windows.Input.Cursors.SizeAll;
        PinStateChanged?.Invoke(this, true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PopupWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var hasControl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (_isEditingTranslation && hasControl && e.Key == Key.Enter)
        {
            TrySaveTranslationCorrection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isEditingTranslation)
            {
                CancelTranslationEditing(showStatus: false);
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (!hasControl)
        {
            return;
        }

        if (e.Key == Key.E && !_isEditingTranslation && EditTranslationButton.IsEnabled)
        {
            BeginTranslationEditing();
            e.Handled = true;
        }
        else if (e.Key == Key.P)
        {
            PinButton_Click(PinButton, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key is Key.OemPlus or Key.Add)
        {
            FontSizeSlider.Value = Math.Min(FontSizeSlider.Maximum, FontSizeSlider.Value + 0.5);
            e.Handled = true;
        }
        else if (e.Key is Key.OemMinus or Key.Subtract)
        {
            FontSizeSlider.Value = Math.Max(FontSizeSlider.Minimum, FontSizeSlider.Value - 0.5);
            e.Handled = true;
        }
    }

    private void BeginTranslationEditing()
    {
        if (!HasTranslation || _isEditingTranslation || !EditTranslationButton.IsEnabled)
        {
            return;
        }

        _translationBeforeEdit = _translatedText;
        _isEditingTranslation = true;
        TranslationRichTextBox.IsReadOnly = false;
        TranslationRichTextBox.IsUndoEnabled = true;
        DirectionButton.IsEnabled = false;
        EditTranslationButton.Tag = "Selected";
        EditTranslationIcon.Visibility = Visibility.Collapsed;
        SaveCorrectionIcon.Visibility = Visibility.Visible;
        ApplyUiLanguage(_uiLanguage);
        ShowActionStatus(ActionMessageKind.CorrectionEditing);
        Activate();
        TranslationRichTextBox.Focus();
        TranslationRichTextBox.CaretPosition = TranslationRichTextBox.Document.ContentEnd;
    }

    private void TrySaveTranslationCorrection()
    {
        if (!_isEditingTranslation)
        {
            return;
        }

        var range = new TextRange(
            TranslationRichTextBox.Document.ContentStart,
            TranslationRichTextBox.Document.ContentEnd);
        var correctedTranslation = range.Text.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(correctedTranslation))
        {
            ShowActionStatus(ActionMessageKind.CorrectionSaveFailed);
            return;
        }

        if (string.Equals(correctedTranslation, _translationBeforeEdit, StringComparison.Ordinal))
        {
            CancelTranslationEditing(showStatus: false);
            return;
        }

        if (CorrectionSaveRequested?.Invoke(this, correctedTranslation) != true)
        {
            ShowActionStatus(ActionMessageKind.CorrectionSaveFailed);
            return;
        }

        FinishTranslationEditing(correctedTranslation);
        ShowActionStatus(ActionMessageKind.CorrectionSaved);
    }

    private void CancelTranslationEditing(bool showStatus)
    {
        if (!_isEditingTranslation)
        {
            return;
        }

        var restoredTranslation = _translationBeforeEdit;
        FinishTranslationEditing(restoredTranslation);
        if (!showStatus)
        {
            HideActionStatus();
        }
    }

    private void FinishTranslationEditing(string translation)
    {
        _isEditingTranslation = false;
        _translationBeforeEdit = string.Empty;
        TranslationRichTextBox.IsReadOnly = true;
        TranslationRichTextBox.IsUndoEnabled = false;
        DirectionButton.IsEnabled = true;
        EditTranslationButton.Tag = null;
        EditTranslationIcon.Visibility = Visibility.Visible;
        SaveCorrectionIcon.Visibility = Visibility.Collapsed;
        _translatedText = translation;
        _lastCompletedTranslation = new CompletedTranslation(
            _currentTranslationSourceText,
            _currentSourceLanguage,
            translation,
            _targetLanguage);
        _translationParagraph = null;
        _renderedTranslation = string.Empty;
        SetTranslationText(translation);
        ApplyUiLanguage(_uiLanguage);
    }

    private void PopupSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsPinned
            || e.ChangedButton != MouseButton.Left
            || TranslationRichTextBox.IsMouseOver
            || ActionPanel.IsMouseOver)
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

    private void SetActionBarVisibility(Visibility visibility)
    {
        ActionBarSurface.Visibility = visibility;
        ActionPanel.Visibility = visibility;
    }

    private void PreserveCurrentWindowSize()
    {
        EnterManualSizeMode(updateLayout: true);
    }

    private void EnterManualSizeMode(bool updateLayout)
    {
        if (updateLayout)
        {
            UpdateLayout();
        }

        var currentWidth = ActualWidth > 0 ? ActualWidth : Width;
        var currentHeight = ActualHeight > 0 ? ActualHeight : Height;

        _isAutomaticSizing = false;
        _lastAutomaticSizeTextLength = 0;
        ConfigureResizableLayout(_anchorPoint);
        if (!double.IsNaN(currentWidth) && currentWidth > 0)
        {
            Width = currentWidth;
        }

        if (!double.IsNaN(currentHeight) && currentHeight > 0)
        {
            Height = currentHeight;
        }
    }

    private void ConfigureResizableLayout(ScreenPoint anchorPoint)
    {
        SizeToContent = SizeToContent.Manual;
        MinWidth = PopupAutoSizeCalculator.MinimumWidth;
        MinHeight = PopupAutoSizeCalculator.MinimumHeight;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        ContentRow.Height = new GridLength(1, GridUnitType.Star);
        PopupSurface.MaxWidth = double.PositiveInfinity;
        PopupSurface.MaxHeight = double.PositiveInfinity;
        PopupSurface.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        PopupSurface.VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
        TranslationRichTextBox.Width = double.NaN;
        TranslationRichTextBox.Height = double.NaN;
        TranslationRichTextBox.MaxHeight = double.PositiveInfinity;
        TranslationRichTextBox.MaxWidth = double.PositiveInfinity;

        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(anchorPoint.X, anchorPoint.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        MaxWidth = Math.Max(MinWidth, (screen.WorkingArea.Width - 16) / dpi.DpiScaleX);
        MaxHeight = Math.Max(MinHeight, (screen.WorkingArea.Height - 16) / dpi.DpiScaleY);
        PopupSurface.MaxWidth = MaxWidth;
        PopupSurface.MaxHeight = MaxHeight;
    }

    private void ApplyAutomaticSize(string text, ScreenPoint anchorPoint, bool force)
    {
        if (!_isAutomaticSizing || IsPinned)
        {
            return;
        }

        if (!force
            && IsVisible
            && text.Length < _lastAutomaticSizeTextLength + AutomaticSizeGrowthThreshold)
        {
            return;
        }

        _lastAutomaticSizeTextLength = text.Length;
        ConfigureResizableLayout(anchorPoint);
        ActionBarSurface.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var targetSize = PopupAutoSizeCalculator.Calculate(
            text,
            FontSizeSlider.Value,
            MaxWidth,
            MaxHeight,
            ActionBarSurface.DesiredSize.Width);

        var currentWidth = IsVisible && ActualWidth > 0 ? ActualWidth : 0;
        var currentHeight = IsVisible && ActualHeight > 0 ? ActualHeight : 0;
        var targetWidth = Math.Max(currentWidth, targetSize.Width);
        var targetHeight = Math.Max(currentHeight, targetSize.Height);
        var sizeChanged = Math.Abs(Width - targetWidth) > 0.5
                          || Math.Abs(Height - targetHeight) > 0.5;
        Width = targetWidth;
        Height = targetHeight;

        if (sizeChanged && IsVisible)
        {
            SchedulePositionNearAnchor();
        }
    }

    private async Task CopyWithFeedbackAsync(WpfButton button, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            WpfClipboard.SetText(text);
            button.ToolTip = Localize("Copied", "已复制");
            ShowActionStatus(ActionMessageKind.CopySucceeded);
        }
        catch (ExternalException)
        {
            button.ToolTip = Localize("Copy failed", "复制失败");
            ShowActionStatus(ActionMessageKind.CopyFailed);
        }

        var feedbackVersion = ++_copyFeedbackVersion;
        try
        {
            await Task.Delay(800, _lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (feedbackVersion == _copyFeedbackVersion)
        {
            ApplyCopyButtonToolTips();
            HideActionStatus();
        }
    }

    private void SetTranslationText(string text)
    {
        var fontSize = FontSizeSlider is null || double.IsNaN(FontSizeSlider.Value)
            ? DefaultTranslationFontSize
            : FontSizeSlider.Value;
        if (_translationParagraph is not null
            && text.StartsWith(_renderedTranslation, StringComparison.Ordinal)
            && text.Length > _renderedTranslation.Length)
        {
            AppendTranslationRuns(_translationParagraph, text[_renderedTranslation.Length..]);
            _renderedTranslation = text;
            ApplyTranslationFontSize(fontSize);
            return;
        }

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = CalculateLineHeight(fontSize),
        };

        AppendTranslationRuns(paragraph, text);

        TranslationRichTextBox.Document = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity,
            ColumnGap = 0,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = TranslationRichTextBox.Foreground,
            FontFamily = _englishTranslationFont,
            FontSize = fontSize,
            TextAlignment = TextAlignment.Left,
        };
        _translationParagraph = paragraph;
        _renderedTranslation = text;
        ApplyTranslationFontSize(fontSize);
    }

    private void AppendTranslationRuns(Paragraph paragraph, string text)
    {
        foreach (var segment in TranslationTypography.Segment(text))
        {
            paragraph.Inlines.Add(new Run(segment.Text)
            {
                FontFamily = segment.UsesChineseFont ? _chineseTranslationFont : _englishTranslationFont,
            });
        }
    }

    private void ShowActionStatus(ActionMessageKind messageKind)
    {
        _actionMessageKind = messageKind;
        UpdateActionMessageText();
        ActionStatusText.Visibility = Visibility.Visible;
    }

    private void HideActionStatus()
    {
        _actionMessageKind = ActionMessageKind.None;
        ActionStatusText.Visibility = Visibility.Collapsed;
    }

    private void UpdateDirectionButtonText()
    {
        var nextTarget = LanguageDirectionResolver.GetOppositeTarget(_targetLanguage);
        DirectionButton.Content = nextTarget == LanguageDirectionResolver.English
            ? Localize("To English", "译为英文")
            : Localize("To Chinese", "译为中文");
    }

    private void UpdateBodyMessageText()
    {
        LoadingTextBlock.Text = _bodyMessageKind switch
        {
            BodyMessageKind.Failure => Localize("Translation failed. Try again.", "翻译失败，请重试"),
            BodyMessageKind.CustomFailure => _customFailureMessage
                ?? Localize("Translation failed. Try again.", "翻译失败，请重试"),
            _ => Localize("Translating…", "正在翻译…"),
        };
    }

    private void UpdateActionMessageText()
    {
        ActionStatusText.Text = _actionMessageKind switch
        {
            ActionMessageKind.Retranslating => Localize("Retranslating…", "正在重新翻译…"),
            ActionMessageKind.CopySucceeded => Localize("✓ Copied", "✓ 已复制"),
            ActionMessageKind.CopyFailed => Localize("Copy failed", "复制失败"),
            ActionMessageKind.TranslationFailedPreserved => Localize(
                "Translation failed · Previous result kept",
                "翻译失败 · 已保留原译文"),
            ActionMessageKind.CorrectionEditing => Localize(
                "Editing · Ctrl+Enter to save · Esc to cancel",
                "正在编辑 · Ctrl+Enter 保存 · Esc 取消"),
            ActionMessageKind.CorrectionSaved => Localize(
                "✓ Saved to translation memory",
                "✓ 已保存到翻译记忆"),
            ActionMessageKind.CorrectionSaveFailed => Localize(
                "Could not save correction",
                "无法保存修正译文"),
            _ => string.Empty,
        };
    }

    private void ApplyCopyButtonToolTips()
    {
        CopySourceButton.ToolTip = Localize("Copy source", "复制原文");
        CopyTranslationButton.ToolTip = Localize("Copy translation", "复制译文");
    }

    private string Localize(string english, string chinese)
    {
        return _uiLanguage == "zh-CN" ? chinese : english;
    }

    private static System.Windows.Media.FontFamily CreateFontFamily(string? requested, string fallback)
    {
        var familyName = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
        if (familyName.Equals("Source Sans Pro", StringComparison.OrdinalIgnoreCase)
            && System.Windows.Application.Current?.TryFindResource("InterfaceFont")
                is System.Windows.Media.FontFamily bundledSourceSans)
        {
            return bundledSourceSans;
        }

        try
        {
            return new System.Windows.Media.FontFamily(familyName);
        }
        catch (ArgumentException)
        {
            return new System.Windows.Media.FontFamily(fallback);
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (double.IsNaN(e.NewValue) || e.NewValue <= 0)
        {
            return;
        }

        if (FontSizeValueText is not null)
        {
            FontSizeValueText.Text = e.NewValue.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }

        ApplyTranslationFontSize(e.NewValue);
        if (_isAutomaticSizing && _translatedText.Length > 0)
        {
            ApplyAutomaticSize(_translatedText, _anchorPoint, force: true);
        }
    }

    private void ApplyTranslationFontSize(double fontSize)
    {
        if (TranslationRichTextBox is null)
        {
            return;
        }

        TranslationRichTextBox.FontSize = fontSize;
        if (TranslationRichTextBox.Document is not { } document)
        {
            return;
        }

        document.FontSize = fontSize;
        var lineHeight = CalculateLineHeight(fontSize);
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                paragraph.LineHeight = lineHeight;
            }
        }
    }

    private static double CalculateLineHeight(double fontSize)
    {
        return Math.Round(fontSize * 1.45, 2);
    }

    private void TranslationRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive)
        {
            Activate();
        }

        TranslationRichTextBox.Focus();
    }

    private TextPointer? GetTranslationPointerAtOffset(int characterOffset)
    {
        if (_translationParagraph is null)
        {
            return null;
        }

        var remaining = characterOffset;
        foreach (var inline in _translationParagraph.Inlines)
        {
            if (inline is not Run run)
            {
                continue;
            }

            var runLength = run.Text.Length;
            if (remaining <= runLength)
            {
                return run.ContentStart.GetPositionAtOffset(remaining, LogicalDirection.Forward)
                    ?? run.ContentEnd;
            }

            remaining -= runLength;
        }

        return _translationParagraph.ContentEnd;
    }

    private void PopupWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible
            || IsPinned
            || SizeToContent == SizeToContent.Manual
            || _isRepositionPending)
        {
            return;
        }

        SchedulePositionNearAnchor();
    }

    private void SchedulePositionNearAnchor()
    {
        if (_isRepositionPending)
        {
            return;
        }

        _isRepositionPending = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _isRepositionPending = false;
                if (IsVisible && !IsPinned)
                {
                    UpdateLayout();
                    PositionNear(_anchorPoint);
                }
            },
            System.Windows.Threading.DispatcherPriority.Render);
    }

    private void PositionNear(ScreenPoint anchorPoint)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        }

        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(anchorPoint.X, anchorPoint.Y));
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        if (NativeMethods.GetWindowRect(_windowHandle, out var currentBounds)
            && currentBounds.Width > 0
            && currentBounds.Height > 0)
        {
            width = currentBounds.Width;
            height = currentBounds.Height;
        }

        const int offsetX = 10;
        const int offsetY = 12;
        const int screenMargin = 8;

        width = Math.Min(width, Math.Max(1, screen.WorkingArea.Width - (screenMargin * 2)));
        height = Math.Min(height, Math.Max(1, screen.WorkingArea.Height - (screenMargin * 2)));

        var x = anchorPoint.X + offsetX;
        var y = anchorPoint.Y + offsetY;
        if (x + width > screen.WorkingArea.Right - screenMargin)
        {
            x = anchorPoint.X - width - offsetX;
        }

        if (y + height > screen.WorkingArea.Bottom - screenMargin)
        {
            y = anchorPoint.Y - height - offsetY;
        }

        var minimumX = screen.WorkingArea.Left + screenMargin;
        var minimumY = screen.WorkingArea.Top + screenMargin;
        x = Math.Clamp(x, minimumX, Math.Max(minimumX, screen.WorkingArea.Right - width - screenMargin));
        y = Math.Clamp(y, minimumY, Math.Max(minimumY, screen.WorkingArea.Bottom - height - screenMargin));

        NativeMethods.SetWindowPos(
            _windowHandle,
            NativeMethods.HwndTopmost,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private enum ActionMessageKind
    {
        None,
        Retranslating,
        CopySucceeded,
        CopyFailed,
        TranslationFailedPreserved,
        CorrectionEditing,
        CorrectionSaved,
        CorrectionSaveFailed,
    }

    private enum BodyMessageKind
    {
        Translating,
        Failure,
        CustomFailure,
    }

    private readonly record struct CompletedTranslation(
        string SourceText,
        string SourceLanguage,
        string TranslatedText,
        string TargetLanguage);
}
