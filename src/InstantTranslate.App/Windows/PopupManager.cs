using InstantTranslate.Models;
using InstantTranslate.Settings;
using InstantTranslate.Translation;

namespace InstantTranslate.Windows;

internal sealed class PopupManager : IPopupPresenter, IDisposable
{
    private readonly Dictionary<long, PopupWindow> _windows = new();
    private readonly Func<PopupAppearanceSettings> _getAppearanceSettings;
    private long? _transientRequestId;
    private bool _disposing;

    public PopupManager(Func<double>? getDefaultFontSize = null)
    {
        _getAppearanceSettings = getDefaultFontSize is null
            ? () => PopupAppearanceSettings.Default
            : () => PopupAppearanceSettings.Default with { FontSize = getDefaultFontSize() };
    }

    public PopupManager(Func<PopupAppearanceSettings> getAppearanceSettings)
    {
        _getAppearanceSettings = getAppearanceSettings
            ?? throw new ArgumentNullException(nameof(getAppearanceSettings));
    }

    public event Action<PopupRetranslateRequest>? RetranslateRequested;

    public event Action<long>? PopupClosed;

    public event Action<long, bool>? PinStateChanged;

    public event Func<PopupTranslationCorrection, bool>? TranslationCorrectionRequested;

    public event Action<PopupExplanationRequest>? ExplanationRequested;

    public event Action<long>? ExplanationDismissed;

    public string CreateStatusReport(bool useChinese)
    {
        var pinnedCount = _windows.Values.Count(window => window.IsPinned);
        return useChinese
            ? $"浮窗状态{Environment.NewLine}总数：{_windows.Count}{Environment.NewLine}保留：{pinnedCount}"
            : $"Popup status{Environment.NewLine}Total: {_windows.Count}{Environment.NewLine}Pinned: {pinnedCount}";
    }

    public void ShowLoading(long requestId, ScreenPoint anchorPoint)
    {
        var window = GetOrCreateWindow(requestId);
        if (!window.IsPinned)
        {
            CloseOtherTransient(requestId);
            _transientRequestId = requestId;
        }

        window.ShowLoading(anchorPoint);
    }

    public void ShowTranslation(
        long requestId,
        string sourceText,
        string translatedText,
        string targetLanguage,
        ScreenPoint anchorPoint,
        string sourceLanguage = "自动检测")
    {
        var window = GetOrCreateWindow(requestId);
        if (!window.IsPinned)
        {
            CloseOtherTransient(requestId);
            _transientRequestId = requestId;
        }

        window.ShowTranslation(
            sourceText,
            translatedText,
            targetLanguage,
            anchorPoint,
            sourceLanguage);
    }

    public void FailRequest(long requestId, string? message = null)
    {
        if (!_windows.TryGetValue(requestId, out var window))
        {
            return;
        }

        if (window.HasTranslation)
        {
            window.RestoreTranslationAfterFailure();
        }
        else
        {
            window.ShowFailure(message);
        }
    }

    public void CompleteRequest(long requestId)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.MarkTranslationComplete();
        }
    }

    public void ShowExplanationLoading(long requestId)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.ShowExplanationLoading();
        }
    }

    public void ShowExplanation(long requestId, string explanation)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.ShowExplanation(explanation);
        }
    }

    public void FailExplanation(long requestId, string message)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.ShowExplanationFailure(message);
        }
    }

    public void HideExplanation(long requestId)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.HideExplanation(notifyDismissed: false);
        }
    }

    public bool IsPointOverPopup(ScreenPoint point)
    {
        return _windows.Values.Any(window => window.ContainsScreenPoint(point));
    }

    public void HideTransientPopup()
    {
        if (_transientRequestId is not { } requestId
            || !_windows.TryGetValue(requestId, out var window)
            || window.IsPinned)
        {
            return;
        }

        window.Close();
    }

    public void HideTransientPopupIfOutside(ScreenPoint point)
    {
        if (_transientRequestId is not { } requestId
            || !_windows.TryGetValue(requestId, out var window)
            || window.IsPinned
            || window.ContainsScreenPoint(point))
        {
            return;
        }

        window.Close();
    }

    internal PopupWindow? GetWindowForVisualTest(long requestId)
    {
        return _windows.GetValueOrDefault(requestId);
    }

    public void ApplyAppearanceToOpenWindows()
    {
        var appearance = _getAppearanceSettings();
        foreach (var window in _windows.Values)
        {
            window.ApplyAppearance(
                appearance.EnglishFontFamily,
                appearance.ChineseFontFamily,
                appearance.UiLanguage,
                appearance.PopupVisualStyle);
        }
    }

    public void Dispose()
    {
        if (_disposing)
        {
            return;
        }

        _disposing = true;
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
        _transientRequestId = null;
    }

    private PopupWindow GetOrCreateWindow(long requestId)
    {
        if (_windows.TryGetValue(requestId, out var existing))
        {
            return existing;
        }

        var appearance = _getAppearanceSettings();
        var window = new PopupWindow(
            requestId,
            appearance.FontSize,
            appearance.EnglishFontFamily,
            appearance.ChineseFontFamily,
            appearance.UiLanguage,
            appearance.PopupVisualStyle);
        window.PinStateChanged += OnPinStateChanged;
        window.RetranslateRequested += OnRetranslateRequested;
        window.CorrectionSaveRequested += OnCorrectionSaveRequested;
        window.ExplanationRequested += OnExplanationRequested;
        window.ExplanationDismissed += OnExplanationDismissed;
        window.Closed += OnWindowClosed;
        _windows.Add(requestId, window);
        return window;
    }

    private void CloseOtherTransient(long requestId)
    {
        if (_transientRequestId is not { } existingId || existingId == requestId)
        {
            return;
        }

        if (_windows.TryGetValue(existingId, out var existingWindow) && !existingWindow.IsPinned)
        {
            existingWindow.Close();
        }
    }

    private void OnPinStateChanged(PopupWindow window, bool isPinned)
    {
        PinStateChanged?.Invoke(window.RequestId, isPinned);
        if (isPinned)
        {
            if (_transientRequestId == window.RequestId)
            {
                _transientRequestId = null;
            }

            return;
        }

        if (window.IsVisible)
        {
            // Unpinning keeps this result visible, but returns it to the normal
            // transient lifecycle so the next outside click can dismiss it.
            CloseOtherTransient(window.RequestId);
            _transientRequestId = window.RequestId;
        }
    }

    private void OnRetranslateRequested(PopupWindow window, string targetLanguage)
    {
        RetranslateRequested?.Invoke(new PopupRetranslateRequest(
            window.RequestId,
            window.LanguageSwitchSourceText,
            window.CurrentTargetLanguage,
            targetLanguage,
            window.AnchorPoint));
    }

    private bool OnCorrectionSaveRequested(PopupWindow window, string correctedTranslation)
    {
        return TranslationCorrectionRequested?.Invoke(new PopupTranslationCorrection(
            window.RequestId,
            window.CurrentTranslationSourceText,
            correctedTranslation,
            window.CurrentSourceLanguage,
            window.CurrentTargetLanguage)) == true;
    }

    private void OnExplanationRequested(
        PopupWindow window,
        string subjectText,
        ExplanationScope scope)
    {
        if (string.IsNullOrWhiteSpace(subjectText)
            || string.IsNullOrWhiteSpace(window.CurrentTranslationSourceText)
            || string.IsNullOrWhiteSpace(window.CurrentTranslationText))
        {
            return;
        }

        ExplanationRequested?.Invoke(new PopupExplanationRequest(
            window.RequestId,
            subjectText,
            window.CurrentTranslationSourceText,
            window.CurrentTranslationText,
            window.CurrentSourceLanguage,
            window.CurrentTargetLanguage,
            scope));
    }

    private void OnExplanationDismissed(PopupWindow window)
    {
        ExplanationDismissed?.Invoke(window.RequestId);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not PopupWindow window)
        {
            return;
        }

        window.PinStateChanged -= OnPinStateChanged;
        window.RetranslateRequested -= OnRetranslateRequested;
        window.CorrectionSaveRequested -= OnCorrectionSaveRequested;
        window.ExplanationRequested -= OnExplanationRequested;
        window.ExplanationDismissed -= OnExplanationDismissed;
        window.Closed -= OnWindowClosed;
        _windows.Remove(window.RequestId);
        if (_transientRequestId == window.RequestId)
        {
            _transientRequestId = null;
        }

        if (!_disposing)
        {
            PopupClosed?.Invoke(window.RequestId);
        }
    }

}

internal sealed record PopupAppearanceSettings(
    double FontSize,
    string EnglishFontFamily,
    string ChineseFontFamily,
    string UiLanguage,
    string PopupVisualStyle)
{
    internal static PopupAppearanceSettings Default { get; } = new(
        16.5,
        "Times New Roman",
        "SimHei",
        "en",
        PopupVisualStyleCatalog.DefaultStyleId);
}
