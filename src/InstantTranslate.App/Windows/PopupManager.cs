using InstantTranslate.Models;

namespace InstantTranslate.Windows;

internal sealed class PopupManager : IPopupPresenter, IDisposable
{
    private readonly Dictionary<long, PopupWindow> _windows = new();
    private long? _transientRequestId;
    private bool _disposing;

    public event Action<PopupRetranslateRequest>? RetranslateRequested;

    public event Action<long>? PopupClosed;

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
        ScreenPoint anchorPoint)
    {
        if (_windows.TryGetValue(requestId, out var window))
        {
            window.ShowTranslation(sourceText, translatedText, targetLanguage, anchorPoint);
        }
    }

    public void FailRequest(long requestId)
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
            window.Close();
        }
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

        var window = new PopupWindow(requestId);
        window.PinStateChanged += OnPinStateChanged;
        window.RetranslateRequested += OnRetranslateRequested;
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
        if (isPinned && _transientRequestId == window.RequestId)
        {
            _transientRequestId = null;
        }
    }

    private void OnRetranslateRequested(PopupWindow window, string targetLanguage)
    {
        RetranslateRequested?.Invoke(new PopupRetranslateRequest(
            window.RequestId,
            window.SourceText,
            targetLanguage,
            window.AnchorPoint));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not PopupWindow window)
        {
            return;
        }

        window.PinStateChanged -= OnPinStateChanged;
        window.RetranslateRequested -= OnRetranslateRequested;
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
