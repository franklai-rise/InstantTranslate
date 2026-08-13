using InstantTranslate.Models;

namespace InstantTranslate.Selection;

internal sealed class SelectionReaderPipeline : ISelectionReader
{
    private static readonly TimeSpan PrimaryReadTimeout = TimeSpan.FromMilliseconds(800);
    private readonly ISelectionReader _primaryReader;
    private readonly ISelectionReader _fallbackReader;
    private readonly ISelectionReader? _clipboardFallbackReader;
    private readonly Func<bool> _allowClipboardFallback;

    public SelectionReaderPipeline(
        ISelectionReader primaryReader,
        ISelectionReader fallbackReader,
        ISelectionReader? clipboardFallbackReader = null,
        Func<bool>? allowClipboardFallback = null)
    {
        _primaryReader = primaryReader ?? throw new ArgumentNullException(nameof(primaryReader));
        _fallbackReader = fallbackReader ?? throw new ArgumentNullException(nameof(fallbackReader));
        _clipboardFallbackReader = clipboardFallbackReader;
        _allowClipboardFallback = allowClipboardFallback ?? (() => true);
    }

    public async Task<string?> TryReadSelectedTextAsync(
        ScreenPoint point,
        CancellationToken cancellationToken)
    {
        string? selectedText = null;
        using (var primaryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            primaryTimeout.CancelAfter(PrimaryReadTimeout);
            try
            {
                selectedText = await _primaryReader
                    .TryReadSelectedTextAsync(point, primaryTimeout.Token)
                    .WaitAsync(PrimaryReadTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Some applications expose UI Automation patterns that block
                // while their renderer is busy. Move on to the non-clipboard
                // reader instead of changing the user's clipboard.
            }
            catch (OperationCanceledException)
                when (primaryTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The short primary-read budget expired; continue with the
                // non-clipboard reader.
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedText))
        {
            return selectedText;
        }

        selectedText = await _fallbackReader
            .TryReadSelectedTextAsync(point, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(selectedText)
            || _clipboardFallbackReader is null
            || !_allowClipboardFallback())
        {
            return selectedText;
        }

        return await _clipboardFallbackReader
            .TryReadSelectedTextAsync(point, cancellationToken)
            .ConfigureAwait(false);
    }
}
