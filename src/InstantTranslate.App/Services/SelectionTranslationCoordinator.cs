using System.Diagnostics;
using System.IO;
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
    private static readonly TimeSpan SelectionReadTimeout = TimeSpan.FromMilliseconds(4000);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FirstContentTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(15);
    private const int PromptVersion = 3;

    private readonly GlobalMouseHook _mouseHook;
    private readonly ISelectionReader _selectionReader;
    private readonly ITranslationProviderFactory _translationProviderFactory;
    private readonly IPopupPresenter _popupPresenter;
    private readonly Dispatcher _dispatcher;
    private readonly Func<AppSettings> _getSettings;
    private readonly TranslationPerformanceMonitor _performanceMonitor;
    private readonly TranslationMemoryStore _translationMemoryStore;
    private readonly RequestVersionGate _requestGate = new();
    private readonly TranslationMemoryCache _translationCache = new();
    private readonly TranslationInFlightRegistry _inFlightTranslations = new();
    private readonly SemaphoreSlim _translationConcurrency = new(3, 3);
    private readonly object _retranslationSync = new();
    private readonly Dictionary<long, CancellationTokenSource> _retranslations = new();
    private readonly Dictionary<long, CancellationTokenSource> _detachedSelections = new();
    private readonly HashSet<long> _runningSelections = new();
    private bool _disposed;

    public SelectionTranslationCoordinator(
        GlobalMouseHook mouseHook,
        ISelectionReader selectionReader,
        ITranslationProviderFactory translationProviderFactory,
        IPopupPresenter popupPresenter,
        Dispatcher dispatcher,
        Func<AppSettings> getSettings,
        TranslationPerformanceMonitor? performanceMonitor = null,
        TranslationMemoryStore? translationMemoryStore = null)
    {
        _mouseHook = mouseHook;
        _selectionReader = selectionReader;
        _translationProviderFactory = translationProviderFactory;
        _popupPresenter = popupPresenter;
        _dispatcher = dispatcher;
        _getSettings = getSettings;
        _performanceMonitor = performanceMonitor ?? new TranslationPerformanceMonitor();
        _translationMemoryStore = translationMemoryStore ?? new TranslationMemoryStore();

        _mouseHook.MousePressed += OnMousePressed;
        _mouseHook.SelectionGestureCompleted += OnSelectionGestureCompleted;
        _popupPresenter.RetranslateRequested += OnRetranslateRequested;
        _popupPresenter.PopupClosed += OnPopupClosed;
        _popupPresenter.PinStateChanged += OnPopupPinStateChanged;
        _popupPresenter.TranslationCorrectionRequested += OnTranslationCorrectionRequested;
    }

    public event Action<string>? TranslationFailed;

    /// <summary>
    /// Translates text the user explicitly copied. This is the safe fallback for
    /// custom-rendered applications such as WeChat that expose no selectable text
    /// through UI Automation and ignore WM_COPY.
    /// </summary>
    public void TranslateText(string? text, ScreenPoint anchorPoint)
    {
        var normalizedText = TextNormalizer.Normalize(text);
        if (_disposed || string.IsNullOrWhiteSpace(normalizedText))
        {
            return;
        }

        _dispatcher.BeginInvoke(
            () =>
            {
                if (_disposed)
                {
                    return;
                }

                var lease = _requestGate.BeginRequest();
                TrackSelection(lease.Version);
                _ = ProcessExplicitTextAsync(normalizedText, anchorPoint, _getSettings(), lease);
            },
            DispatcherPriority.Send);
    }

    private void OnMousePressed(ScreenPoint point)
    {
        _dispatcher.BeginInvoke(
            () =>
            {
                if (_disposed
                    || _popupPresenter.IsPointOverPopup(point)
                    || WindowProcessResolver.IsCurrentProcessAt(point))
                {
                    return;
                }

                _requestGate.CancelActive();
                _popupPresenter.HideTransientPopup();
            },
            DispatcherPriority.Send);
    }

    private void OnSelectionGestureCompleted(SelectionGesture gesture)
    {
        _dispatcher.BeginInvoke(
            () => BeginSelectionIfExternal(gesture),
            DispatcherPriority.Send);
    }

    private void BeginSelectionIfExternal(SelectionGesture gesture)
    {
        if (_disposed
            || _popupPresenter.IsPointOverPopup(gesture.Start)
            || WindowProcessResolver.IsCurrentProcessAt(gesture.Start)
            || _popupPresenter.IsPointOverPopup(gesture.End)
            || WindowProcessResolver.IsCurrentProcessAt(gesture.End)
            || !WindowProcessResolver.ArePointsInSameExternalProcess(gesture.Start, gesture.End))
        {
            return;
        }

        var settings = _getSettings();
        if (!settings.IsEnabled)
        {
            return;
        }

        var lease = _requestGate.BeginRequest();
        TrackSelection(lease.Version);
        _ = ProcessSelectionAsync(gesture, settings, lease);
    }

    private async Task ProcessSelectionAsync(
        SelectionGesture gesture,
        AppSettings settings,
        RequestLease lease)
    {
        var performance = _performanceMonitor.Begin(
            TranslationTrigger.Selection,
            settings.ProviderId);
        var outcome = TranslationOutcome.Cancelled;
        try
        {
            if (settings.SelectionDelayMilliseconds > 0)
            {
                await Task.Delay(settings.SelectionDelayMilliseconds, lease.CancellationToken).ConfigureAwait(false);
            }

            var selectionReadStartedAt = Stopwatch.GetTimestamp();
            SelectionCapture? capture;
            try
            {
                capture = _selectionReader is IContextualSelectionReader contextualReader
                    ? await contextualReader
                        .TryReadSelectionAsync(
                            gesture.End,
                            settings.UseSelectionContext,
                            lease.CancellationToken)
                        .WaitAsync(SelectionReadTimeout, lease.CancellationToken)
                        .ConfigureAwait(false)
                    : await ReadPlainSelectionAsync(
                            _selectionReader,
                            gesture.End,
                            lease.CancellationToken)
                        .WaitAsync(SelectionReadTimeout, lease.CancellationToken)
                        .ConfigureAwait(false);
            }
            finally
            {
                performance.MarkSelectionRead(Stopwatch.GetElapsedTime(selectionReadStartedAt));
            }
            var selectedText = capture?.Text;
            if (string.IsNullOrWhiteSpace(selectedText) || !_requestGate.IsCurrent(lease.Version))
            {
                return;
            }

            var targetLanguage = LanguageDirectionResolver.ResolveTargetLanguage(selectedText, settings);
            outcome = await TranslateResolvedTextAsync(
                    lease.Version,
                    selectedText,
                    capture?.Context,
                    settings.SourceLanguage,
                    targetLanguage,
                    gesture.End,
                    settings,
                    lease.CancellationToken,
                    () => IsSelectionRequestActive(lease.Version),
                    performance)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate selection request ended unexpectedly: {exception}");
            await FailPopupIfCurrentAsync(lease.Version, Localize(settings, "The translation ended unexpectedly. Try again.", "翻译请求意外中断，请重试。"));
        }
        catch (TimeoutException)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine("InstantTranslate: selection read timed out.");
            await FailPopupIfCurrentAsync(lease.Version, Localize(settings, "Reading the selection timed out. Select the text again.", "读取选中文字超时，请重新选择。"));
        }
        catch (TranslationProviderException exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate translation provider failed: {exception}");
            if (IsSelectionRequestActive(lease.Version))
            {
                var message = UiLanguageCatalog.LocalizeProviderError(settings.UiLanguage, exception.Message);
                await FailPopupIfCurrentAsync(lease.Version, message);
                TranslationFailed?.Invoke(message);
            }
        }
        catch (Exception exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate selection pipeline failed: {exception}");
            await FailPopupIfCurrentAsync(lease.Version, Localize(settings, "Translation failed. Check the network and try again.", "翻译失败，请检查网络后重试。"));
        }
        finally
        {
            performance.Complete(outcome);
            CompleteSelection(lease.Version);
        }
    }

    private async Task ProcessExplicitTextAsync(
        string text,
        ScreenPoint anchorPoint,
        AppSettings settings,
        RequestLease lease)
    {
        var performance = _performanceMonitor.Begin(
            TranslationTrigger.Clipboard,
            settings.ProviderId);
        var outcome = TranslationOutcome.Cancelled;
        try
        {
            var targetLanguage = LanguageDirectionResolver.ResolveTargetLanguage(text, settings);
            outcome = await TranslateResolvedTextAsync(
                    lease.Version,
                    text,
                    context: null,
                    settings.SourceLanguage,
                    targetLanguage,
                    anchorPoint,
                    settings,
                    lease.CancellationToken,
                    () => IsSelectionRequestActive(lease.Version),
                    performance)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
        {
        }
        catch (TranslationProviderException exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate explicit translation failed: {exception}");
            if (IsSelectionRequestActive(lease.Version))
            {
                var message = UiLanguageCatalog.LocalizeProviderError(settings.UiLanguage, exception.Message);
                await FailPopupIfCurrentAsync(lease.Version, message);
                TranslationFailed?.Invoke(message);
            }
        }
        catch (Exception exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate explicit translation pipeline failed: {exception}");
            await FailPopupIfCurrentAsync(lease.Version, Localize(settings, "Translation failed. Check the network and try again.", "翻译失败，请检查网络后重试。"));
        }
        finally
        {
            performance.Complete(outcome);
            CompleteSelection(lease.Version);
        }
    }

    private void OnRetranslateRequested(PopupRetranslateRequest request)
    {
        if (_disposed || string.IsNullOrWhiteSpace(request.SourceText))
        {
            return;
        }

        CancelSelectionRequest(request.RequestId);

        var cancellation = new CancellationTokenSource();
        lock (_retranslationSync)
        {
            if (_retranslations.Remove(request.RequestId, out var previous))
            {
                previous.Cancel();
            }

            _retranslations[request.RequestId] = cancellation;
        }

        _ = ProcessRetranslationAsync(request, _getSettings(), cancellation);
    }

    private async Task ProcessRetranslationAsync(
        PopupRetranslateRequest request,
        AppSettings settings,
        CancellationTokenSource cancellation)
    {
        var performance = _performanceMonitor.Begin(
            TranslationTrigger.Retranslation,
            settings.ProviderId);
        var outcome = TranslationOutcome.Cancelled;
        try
        {
            outcome = await TranslateResolvedTextAsync(
                    request.RequestId,
                    request.SourceText,
                    context: null,
                    request.SourceLanguage,
                    request.TargetLanguage,
                    request.AnchorPoint,
                    settings,
                    cancellation.Token,
                    () => IsCurrentRetranslation(request.RequestId, cancellation),
                    performance)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate retranslation ended unexpectedly: {exception}");
            if (IsCurrentRetranslation(request.RequestId, cancellation))
            {
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.FailRequest(request.RequestId, Localize(settings, "The translation ended unexpectedly. Try again.", "翻译请求意外中断，请重试。")),
                    DispatcherPriority.Send);
            }
        }
        catch (TranslationProviderException exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate retranslation failed: {exception}");
            if (IsCurrentRetranslation(request.RequestId, cancellation))
            {
                var message = UiLanguageCatalog.LocalizeProviderError(settings.UiLanguage, exception.Message);
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.FailRequest(request.RequestId, message),
                    DispatcherPriority.Send);
                TranslationFailed?.Invoke(message);
            }
        }
        catch (Exception exception)
        {
            outcome = TranslationOutcome.Failed;
            Debug.WriteLine($"InstantTranslate retranslation pipeline failed: {exception}");
            if (IsCurrentRetranslation(request.RequestId, cancellation))
            {
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.FailRequest(request.RequestId, Localize(settings, "Translation failed. Check the network and try again.", "翻译失败，请检查网络后重试。")),
                    DispatcherPriority.Send);
            }
        }
        finally
        {
            performance.Complete(outcome);
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

    private async Task<TranslationOutcome> TranslateResolvedTextAsync(
        long requestId,
        string sourceText,
        string? context,
        string sourceLanguage,
        string targetLanguage,
        ScreenPoint anchorPoint,
        AppSettings settings,
        CancellationToken cancellationToken,
        Func<bool> canPresent,
        TranslationPerformanceOperation performance)
    {
        if (sourceText.Length > settings.MaximumSelectionCharacters)
        {
            await _dispatcher.InvokeAsync(
                () =>
                {
                    _popupPresenter.ShowLoading(requestId, anchorPoint);
                    _popupPresenter.FailRequest(
                        requestId,
                        Localize(
                            settings,
                            $"The selection is too long ({sourceText.Length} characters). The limit is {settings.MaximumSelectionCharacters}; select less text.",
                            $"选中文字过长（{sourceText.Length} 字符），上限为 {settings.MaximumSelectionCharacters}。请缩小选区。"));
                },
                DispatcherPriority.Send,
                cancellationToken);
            return TranslationOutcome.Failed;
        }

        performance.MarkTranslationStarted();
        var savedTranslation = _translationMemoryStore.FindExact(
            sourceText,
            sourceLanguage,
            targetLanguage);
        if (savedTranslation is not null)
        {
            performance.MarkCacheHit();
            performance.MarkFirstContent();
            if (!canPresent())
            {
                return TranslationOutcome.Cancelled;
            }

            await _dispatcher.InvokeAsync(
                () =>
                {
                    _popupPresenter.ShowTranslation(
                        requestId,
                        sourceText,
                        savedTranslation.TargetText,
                        targetLanguage,
                        anchorPoint,
                        sourceLanguage);
                    _popupPresenter.CompleteRequest(requestId);
                },
                DispatcherPriority.Send,
                cancellationToken);
            return TranslationOutcome.Succeeded;
        }

        var preparedOptions = PrepareTranslationOptions(
            sourceText,
            sourceLanguage,
            targetLanguage,
            settings);
        var cacheKey = CreateCacheKey(
            sourceText,
            context,
            sourceLanguage,
            targetLanguage,
            settings,
            preparedOptions);
        if (_translationCache.TryGet(cacheKey, out var cachedTranslation))
        {
            performance.MarkCacheHit();
            performance.MarkFirstContent();
            if (!canPresent())
            {
                return TranslationOutcome.Cancelled;
            }

            await _dispatcher.InvokeAsync(
                () =>
                {
                    _popupPresenter.ShowTranslation(
                        requestId,
                        sourceText,
                        cachedTranslation,
                        targetLanguage,
                        anchorPoint,
                        sourceLanguage);
                    _popupPresenter.CompleteRequest(requestId);
                },
                DispatcherPriority.Send,
                cancellationToken);
            return TranslationOutcome.Succeeded;
        }

        while (_inFlightTranslations.TryJoin(cacheKey, out var pendingTranslation)
               && pendingTranslation is not null)
        {
            performance.MarkCoalesced();
            string sharedTranslation;
            try
            {
                sharedTranslation = await pendingTranslation
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _inFlightTranslations.TryRemove(cacheKey, pendingTranslation);
                continue;
            }

            if (!canPresent())
            {
                return TranslationOutcome.Cancelled;
            }

            performance.MarkFirstContent();
            await _dispatcher.InvokeAsync(
                () =>
                {
                    _popupPresenter.ShowTranslation(
                        requestId,
                        sourceText,
                        sharedTranslation,
                        targetLanguage,
                        anchorPoint,
                        sourceLanguage);
                    _popupPresenter.CompleteRequest(requestId);
                },
                DispatcherPriority.Send,
                cancellationToken);
            return TranslationOutcome.Succeeded;
        }

        var sharedCompletion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ = sharedCompletion.Task.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        if (!_inFlightTranslations.TryRegister(cacheKey, sharedCompletion.Task))
        {
            return await TranslateResolvedTextAsync(
                    requestId,
                    sourceText,
                    context,
                    sourceLanguage,
                    targetLanguage,
                    anchorPoint,
                    settings,
                    cancellationToken,
                    canPresent,
                    performance)
                .ConfigureAwait(false);
        }

        try
        {
            // Do not create a popup until a real selection has been read. Cache hits
            // skip loading entirely; network requests retain the immediate feedback.
            await _dispatcher.InvokeAsync(
                () => _popupPresenter.ShowLoading(requestId, anchorPoint),
                DispatcherPriority.Send,
                cancellationToken);

            var translation = await StreamTranslationAsync(
                    requestId,
                    sourceText,
                    context,
                    sourceLanguage,
                    targetLanguage,
                    anchorPoint,
                    settings,
                    preparedOptions,
                    cacheKey,
                    cancellationToken,
                    canPresent,
                    performance)
                .ConfigureAwait(false);
            if (translation is null)
            {
                sharedCompletion.TrySetCanceled(cancellationToken);
                return TranslationOutcome.Cancelled;
            }

            sharedCompletion.TrySetResult(translation);
            await _dispatcher.InvokeAsync(
                () => _popupPresenter.CompleteRequest(requestId),
                DispatcherPriority.Send,
                cancellationToken);
            return TranslationOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            sharedCompletion.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            sharedCompletion.TrySetException(exception);
            throw;
        }
        finally
        {
            _inFlightTranslations.TryRemove(cacheKey, sharedCompletion.Task);
        }
    }

    private async Task<string?> StreamTranslationAsync(
        long requestId,
        string sourceText,
        string? context,
        string sourceLanguage,
        string targetLanguage,
        ScreenPoint anchorPoint,
        AppSettings settings,
        PreparedTranslationOptions preparedOptions,
        TranslationCacheKey cacheKey,
        CancellationToken cancellationToken,
        Func<bool> canPresent,
        TranslationPerformanceOperation performance)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TranslationTimeout);
        var request = new TranslationRequest(
            sourceText,
            sourceLanguage,
            targetLanguage,
            context,
            preparedOptions.Mode,
            preparedOptions.Tone,
            PersonalGlossary: string.Empty,
            ApplicableGlossaryEntries: preparedOptions.ApplicableGlossaryEntries,
            TranslationExamples: preparedOptions.TranslationExamples);
        var translationProvider = _translationProviderFactory.Create(settings);
        var translation = new StringBuilder();
        var updateThrottle = new StreamingUpdateThrottle();
        var enteredConcurrencySlot = false;

        try
        {
            performance.MarkQueueStarted();
            await _translationConcurrency.WaitAsync(timeout.Token).ConfigureAwait(false);
            enteredConcurrencySlot = true;
            performance.MarkQueueCompleted();
            await using var enumerator = translationProvider
                .TranslateAsync(request, timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            var receivedContent = false;
            while (await MoveNextWithStageTimeoutAsync(
                       enumerator,
                       receivedContent,
                       timeout).ConfigureAwait(false))
            {
                var chunk = enumerator.Current;
                if (!canPresent())
                {
                    return null;
                }

                translation.Append(chunk.TextDelta);
                if (!receivedContent && chunk.TextDelta.Length > 0)
                {
                    receivedContent = true;
                    performance.MarkFirstContent();
                }
                if (!updateThrottle.ShouldPublish(translation.Length, chunk.IsFinal))
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
                        anchorPoint,
                        sourceLanguage),
                    DispatcherPriority.Normal,
                    timeout.Token);
            }

            if (updateThrottle.HasPendingUpdate(translation.Length) && canPresent())
            {
                var finalText = translation.ToString();
                await _dispatcher.InvokeAsync(
                    () => _popupPresenter.ShowTranslation(
                        requestId,
                        sourceText,
                        finalText,
                        targetLanguage,
                        anchorPoint,
                        sourceLanguage),
                    DispatcherPriority.Normal,
                    timeout.Token);
            }

            if (translation.Length > 0)
            {
                var completedTranslation = translation.ToString();
                _translationCache.Set(cacheKey, completedTranslation);
                return completedTranslation;
            }

            return null;
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TranslationProviderException(
                "DeepSeek 翻译请求超过 60 秒，已取消。",
                TranslationFailureKind.Timeout);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationProviderException(
                "DeepSeek 翻译请求意外中断，请重试。",
                exception,
                TranslationFailureKind.Connectivity);
        }
        finally
        {
            if (enteredConcurrencySlot)
            {
                _translationConcurrency.Release();
            }
        }
    }

    private static async Task<bool> MoveNextWithStageTimeoutAsync(
        IAsyncEnumerator<TranslationChunk> enumerator,
        bool receivedContent,
        CancellationTokenSource requestTimeout)
    {
        var stageTimeout = receivedContent ? StreamIdleTimeout : FirstContentTimeout;
        try
        {
            return await enumerator.MoveNextAsync()
                .AsTask()
                .WaitAsync(stageTimeout, requestTimeout.Token)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            requestTimeout.Cancel();
            throw new TranslationProviderException(
                receivedContent
                    ? "DeepSeek 流式响应停顿超过 15 秒，已取消。"
                    : "DeepSeek 在 20 秒内未返回首段译文，请检查网络后重试。",
                exception,
                TranslationFailureKind.Timeout);
        }
    }

    private static TranslationCacheKey CreateCacheKey(
        string sourceText,
        string? context,
        string sourceLanguage,
        string targetLanguage,
        AppSettings settings,
        PreparedTranslationOptions preparedOptions)
    {
        var applicableGlossary = string.Join(
            '\n',
            preparedOptions.ApplicableGlossaryEntries
                .Select(entry => $"{entry.Source}=>{entry.Target}"));
        var optionsSignature = string.Join(
            '\u001F',
            context ?? string.Empty,
            preparedOptions.Mode,
            preparedOptions.Tone,
            applicableGlossary,
            string.Join(
                '\u001E',
                preparedOptions.TranslationExamples.Select(example =>
                    $"{example.SourceText}\u001D{example.TargetText}")));
        return TranslationCacheKey.Create(
            settings.ProviderId,
            settings.DeepSeekEndpoint,
            settings.DeepSeekModel,
            sourceLanguage,
            targetLanguage,
            sourceText,
            PromptVersion,
            optionsSignature);
    }

    private PreparedTranslationOptions PrepareTranslationOptions(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        AppSettings settings)
    {
        var applicableGlossaryEntries = PersonalGlossary.Parse(settings.PersonalGlossary)
            .Where(entry => sourceText.Contains(entry.Source, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var translationExamples = _translationMemoryStore
            .FindRelevant(
                sourceText,
                sourceLanguage,
                targetLanguage)
            .Select(entry => new TranslationExample(entry.SourceText, entry.TargetText))
            .ToArray();
        return new PreparedTranslationOptions(
            TranslationPreferenceCatalog.NormalizeMode(settings.TranslationMode),
            TranslationPreferenceCatalog.NormalizeTone(settings.TranslationTone),
            applicableGlossaryEntries,
            translationExamples);
    }

    private static async Task<SelectionCapture?> ReadPlainSelectionAsync(
        ISelectionReader reader,
        ScreenPoint point,
        CancellationToken cancellationToken)
    {
        var text = await reader.TryReadSelectedTextAsync(point, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : new SelectionCapture(text);
    }

    private static string Localize(AppSettings settings, string english, string chinese)
    {
        return settings.UiLanguage == UiLanguageCatalog.SimplifiedChineseLanguageId
            ? chinese
            : english;
    }

    private sealed record PreparedTranslationOptions(
        string Mode,
        string Tone,
        IReadOnlyList<GlossaryEntry> ApplicableGlossaryEntries,
        IReadOnlyList<TranslationExample> TranslationExamples);

    private void OnPopupClosed(long requestId)
    {
        CancelSelectionRequest(requestId);
        lock (_retranslationSync)
        {
            if (_retranslations.Remove(requestId, out var cancellation))
            {
                cancellation.Cancel();
            }
        }
    }

    private bool OnTranslationCorrectionRequested(PopupTranslationCorrection correction)
    {
        try
        {
            var settings = _getSettings();
            _translationMemoryStore.AddOrUpdate(
                correction.SourceText,
                correction.CorrectedTranslation,
                correction.SourceLanguage,
                correction.TargetLanguage);
            _translationCache.Clear();
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Security.Cryptography.CryptographicException
            or ArgumentException)
        {
            Debug.WriteLine($"InstantTranslate could not save a translation correction: {exception}");
            TranslationFailed?.Invoke(Localize(
                _getSettings(),
                "Could not save the correction securely.",
                "无法安全保存修正译文。"));
            return false;
        }
    }

    private void OnPopupPinStateChanged(long requestId, bool isPinned)
    {
        if (!isPinned || !_requestGate.TryDetach(requestId, out var cancellation) || cancellation is null)
        {
            return;
        }

        lock (_retranslationSync)
        {
            if (_runningSelections.Contains(requestId))
            {
                _detachedSelections[requestId] = cancellation;
            }
            else
            {
                cancellation.Dispose();
            }
        }
    }

    private bool IsSelectionRequestActive(long requestId)
    {
        if (_requestGate.IsCurrent(requestId))
        {
            return true;
        }

        lock (_retranslationSync)
        {
            return !_disposed
                && _detachedSelections.TryGetValue(requestId, out var cancellation)
                && !cancellation.IsCancellationRequested;
        }
    }

    private void CancelSelectionRequest(long requestId)
    {
        _requestGate.CancelIfCurrent(requestId);
        lock (_retranslationSync)
        {
            if (_detachedSelections.TryGetValue(requestId, out var cancellation))
            {
                cancellation.Cancel();
                if (!_runningSelections.Contains(requestId))
                {
                    _detachedSelections.Remove(requestId);
                    cancellation.Dispose();
                }
            }
        }
    }

    private void TrackSelection(long requestId)
    {
        lock (_retranslationSync)
        {
            _runningSelections.Add(requestId);
        }
    }

    private void CompleteSelection(long requestId)
    {
        lock (_retranslationSync)
        {
            _runningSelections.Remove(requestId);
            if (_detachedSelections.Remove(requestId, out var cancellation))
            {
                cancellation.Dispose();
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

    private async Task FailPopupIfCurrentAsync(long version, string? message = null)
    {
        if (!IsSelectionRequestActive(version))
        {
            return;
        }

        await _dispatcher.InvokeAsync(
            () =>
            {
                if (IsSelectionRequestActive(version))
                {
                    _popupPresenter.FailRequest(version, message);
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
        _popupPresenter.PinStateChanged -= OnPopupPinStateChanged;
        _popupPresenter.TranslationCorrectionRequested -= OnTranslationCorrectionRequested;
        _requestGate.Dispose();
        _translationCache.Clear();

        lock (_retranslationSync)
        {
            foreach (var cancellation in _retranslations.Values)
            {
                cancellation.Cancel();
            }

            _retranslations.Clear();

            foreach (var (requestId, cancellation) in _detachedSelections.ToArray())
            {
                cancellation.Cancel();
                if (!_runningSelections.Contains(requestId))
                {
                    _detachedSelections.Remove(requestId);
                    cancellation.Dispose();
                }
            }
        }
    }
}
