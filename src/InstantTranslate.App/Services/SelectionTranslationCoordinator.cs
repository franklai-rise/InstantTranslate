using System.Diagnostics;
using System.Text;
using System.Windows.Threading;
using InstantTranslate.Hooks;
using InstantTranslate.Models;
using InstantTranslate.Selection;
using InstantTranslate.Settings;
using InstantTranslate.Translation;
using InstantTranslate.Windows;

namespace InstantTranslate.Services;

internal sealed class SelectionTranslationCoordinator : IDisposable
{
    private static readonly TimeSpan SelectionReadTimeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromSeconds(30);

    private readonly GlobalMouseHook _mouseHook;
    private readonly ISelectionReader _selectionReader;
    private readonly ITranslationProviderFactory _translationProviderFactory;
    private readonly IPopupPresenter _popupPresenter;
    private readonly Dispatcher _dispatcher;
    private readonly Func<AppSettings> _getSettings;
    private readonly RequestVersionGate _requestGate = new();
    private readonly object _retranslationSync = new();
    private readonly Dictionary<long, CancellationTokenSource> _retranslations = new();
    private bool _disposed;

    public SelectionTranslationCoordinator(
        GlobalMouseHook mouseHook,
        ISelectionReader selectionReader,
        ITranslationProviderFactory translationProviderFactory,
        IPopupPresenter popupPresenter,
        Dispatcher dispatcher,
        Func<AppSettings> getSettings)
    {
        _mouseHook = mouseHook;
        _selectionReader = selectionReader;
        _translationProviderFactory = translationProviderFactory;
        _popupPresenter = popupPresenter;
        _dispatcher = dispatcher;
        _getSettings = getSettings;

        _mouseHook.MousePressed += OnMousePressed;
        _mouseHook.SelectionGestureCompleted += OnSelectionGestureCompleted;
        _popupPresenter.RetranslateRequested += OnRetranslateRequested;
        _popupPresenter.PopupClosed += OnPopupClosed;
    }

    public event Action<string>? TranslationFailed;

    private void OnMousePressed(ScreenPoint point)
    {
        if (WindowProcessResolver.IsCurrentProcessAt(point))
        {
            return;
        }

        _requestGate.CancelActive();
        _dispatcher.BeginInvoke(_popupPresenter.HideTransientPopup, DispatcherPriority.Send);
    }

    private void OnSelectionGestureCompleted(SelectionGesture gesture)
    {
        if (_disposed)
        {
            return;
        }

        var settings = _getSettings();
        if (!settings.IsEnabled || WindowProcessResolver.IsCurrentProcessAt(gesture.End))
        {
            return;
        }

        var lease = _requestGate.BeginRequest();
        _dispatcher.BeginInvoke(
            () =>
            {
                if (_requestGate.IsCurrent(lease.Version))
                {
                    _popupPresenter.ShowLoading(lease.Version, gesture.End);
                }
            },
            DispatcherPriority.Send);
        _ = ProcessSelectionAsync(gesture, settings, lease);
    }

    private async Task ProcessSelectionAsync(
        SelectionGesture gesture,
        AppSettings settings,
        RequestLease lease)
    {
        try
        {
            if (settings.SelectionDelayMilliseconds > 0)
            {
                await Task.Delay(settings.SelectionDelayMilliseconds, lease.CancellationToken).ConfigureAwait(false);
            }

            var selectedText = await _selectionReader
                .TryReadSelectedTextAsync(gesture.End, lease.CancellationToken)
                .WaitAsync(SelectionReadTimeout, lease.CancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(selectedText) || !_requestGate.IsCurrent(lease.Version))
            {
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    FailPopupIfCurrent(lease.Version);
                }

                return;
            }

            var targetLanguage = LanguageDirectionResolver.ResolveTargetLanguage(selectedText, settings);
            await StreamTranslationAsync(
                    lease.Version,
                    selectedText,
                    targetLanguage,
                    gesture.End,
                    settings,
                    lease.CancellationToken,
                    () => _requestGate.IsCurrent(lease.Version))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("InstantTranslate: UI Automation selection read timed out.");
            FailPopupIfCurrent(lease.Version);
        }
        catch (TranslationProviderException exception)
        {
            Debug.WriteLine($"InstantTranslate translation provider failed: {exception}");
            if (_requestGate.IsCurrent(lease.Version))
            {
                FailPopupIfCurrent(lease.Version);
                TranslationFailed?.Invoke(exception.Message);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"InstantTranslate selection pipeline failed: {exception}");
            FailPopupIfCurrent(lease.Version);
        }
    }

    private void OnRetranslateRequested(PopupRetranslateRequest request)
    {
        if (_disposed || string.IsNullOrWhiteSpace(request.SourceText))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        lock (_retranslationSync)
        {
            if (_retranslations.Remove(request.RequestId, out var previous))
            {
                previous.Cancel();
            }

            _retranslations[request.RequestId] = cancellation;
        }

        _popupPresenter.ShowLoading(request.RequestId, request.AnchorPoint);
        _ = ProcessRetranslationAsync(request, _getSettings(), cancellation);
    }

    private async Task ProcessRetranslationAsync(
        PopupRetranslateRequest request,
        AppSettings settings,
        CancellationTokenSource cancellation)
    {
        try
        {
            await StreamTranslationAsync(
                    request.RequestId,
                    request.SourceText,
                    request.TargetLanguage,
                    request.AnchorPoint,
                    settings,
                    cancellation.Token,
                    () => IsCurrentRetranslation(request.RequestId, cancellation))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TranslationProviderException exception)
        {
            Debug.WriteLine($"InstantTranslate retranslation failed: {exception}");
            if (IsCurrentRetranslation(request.RequestId, cancellation))
            {
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.FailRequest(request.RequestId),
                    DispatcherPriority.Send);
                TranslationFailed?.Invoke(exception.Message);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"InstantTranslate retranslation pipeline failed: {exception}");
            if (IsCurrentRetranslation(request.RequestId, cancellation))
            {
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.FailRequest(request.RequestId),
                    DispatcherPriority.Send);
            }
        }
        finally
        {
            lock (_retranslationSync)
            {
                if (_retranslations.TryGetValue(request.RequestId, out var current)
                    && ReferenceEquals(current, cancellation))
                {
                    _retranslations.Remove(request.RequestId);
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task StreamTranslationAsync(
        long requestId,
        string sourceText,
        string targetLanguage,
        ScreenPoint anchorPoint,
        AppSettings settings,
        CancellationToken cancellationToken,
        Func<bool> canPresent)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TranslationTimeout);
        var request = new TranslationRequest(sourceText, settings.SourceLanguage, targetLanguage);
        var translationProvider = _translationProviderFactory.Create(settings);
        var translation = new StringBuilder();

        try
        {
            await foreach (var chunk in translationProvider
                               .TranslateAsync(request, timeout.Token)
                               .WithCancellation(timeout.Token)
                               .ConfigureAwait(false))
            {
                if (!canPresent())
                {
                    return;
                }

                translation.Append(chunk.TextDelta);
                if (translation.Length == 0)
                {
                    continue;
                }

                var currentText = translation.ToString();
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.ShowTranslation(
                        requestId,
                        sourceText,
                        currentText,
                        targetLanguage,
                        anchorPoint),
                    DispatcherPriority.Normal,
                    timeout.Token);
            }
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TranslationProviderException("DeepSeek 翻译请求超过 30 秒，已取消。");
        }
    }

    private void OnPopupClosed(long requestId)
    {
        lock (_retranslationSync)
        {
            if (_retranslations.Remove(requestId, out var cancellation))
            {
                cancellation.Cancel();
            }
        }
    }

    private bool IsCurrentRetranslation(long requestId, CancellationTokenSource cancellation)
    {
        lock (_retranslationSync)
        {
            return !_disposed
                   && _retranslations.TryGetValue(requestId, out var current)
                   && ReferenceEquals(current, cancellation)
                   && !cancellation.IsCancellationRequested;
        }
    }

    private void FailPopupIfCurrent(long version)
    {
        if (!_requestGate.IsCurrent(version))
        {
            return;
        }

        _dispatcher.BeginInvoke(
            () =>
            {
                if (_requestGate.IsCurrent(version))
                {
                    _popupPresenter.FailRequest(version);
                }
            },
            DispatcherPriority.Send);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mouseHook.MousePressed -= OnMousePressed;
        _mouseHook.SelectionGestureCompleted -= OnSelectionGestureCompleted;
        _popupPresenter.RetranslateRequested -= OnRetranslateRequested;
        _popupPresenter.PopupClosed -= OnPopupClosed;
        _requestGate.Dispose();

        lock (_retranslationSync)
        {
            foreach (var cancellation in _retranslations.Values)
            {
                cancellation.Cancel();
            }

            _retranslations.Clear();
        }
    }
}
