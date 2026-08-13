using InstantTranslate.Models;
using InstantTranslate.Selection;

namespace InstantTranslate.Tests;

public sealed class SelectionReaderPipelineTests
{
    [Fact]
    public async Task ReturnsUiaTextWithoutInvokingFallback()
    {
        var primary = new StubSelectionReader("来自 UI Automation");
        var fallback = new StubSelectionReader("来自剪贴板");
        var pipeline = new SelectionReaderPipeline(primary, fallback);

        var result = await pipeline.TryReadSelectedTextAsync(new ScreenPoint(10, 20), CancellationToken.None);

        Assert.Equal("来自 UI Automation", result);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task UsesFallbackWhenUiaReturnsNoText()
    {
        var primary = new StubSelectionReader(null);
        var fallback = new StubSelectionReader("来自剪贴板");
        var pipeline = new SelectionReaderPipeline(primary, fallback);

        var result = await pipeline.TryReadSelectedTextAsync(new ScreenPoint(10, 20), CancellationToken.None);

        Assert.Equal("来自剪贴板", result);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task UsesFallbackWhenUiaReaderBlocks()
    {
        var primary = new BlockingSelectionReader();
        var fallback = new StubSelectionReader("来自剪贴板");
        var pipeline = new SelectionReaderPipeline(primary, fallback);

        var result = await pipeline.TryReadSelectedTextAsync(new ScreenPoint(10, 20), CancellationToken.None);

        Assert.Equal("来自剪贴板", result);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task DoesNotInvokeClipboardFallbackWhenDisabled()
    {
        var primary = new StubSelectionReader(null);
        var nativeFallback = new StubSelectionReader(null);
        var clipboardFallback = new StubSelectionReader("来自剪贴板");
        var pipeline = new SelectionReaderPipeline(
            primary,
            nativeFallback,
            clipboardFallback,
            () => false);

        var result = await pipeline.TryReadSelectedTextAsync(new ScreenPoint(10, 20), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, nativeFallback.CallCount);
        Assert.Equal(0, clipboardFallback.CallCount);
    }

    [Fact]
    public async Task InvokesClipboardFallbackOnlyWhenExplicitlyEnabled()
    {
        var primary = new StubSelectionReader(null);
        var nativeFallback = new StubSelectionReader(null);
        var clipboardFallback = new StubSelectionReader("来自剪贴板");
        var pipeline = new SelectionReaderPipeline(
            primary,
            nativeFallback,
            clipboardFallback,
            () => true);

        var result = await pipeline.TryReadSelectedTextAsync(new ScreenPoint(10, 20), CancellationToken.None);

        Assert.Equal("来自剪贴板", result);
        Assert.Equal(1, clipboardFallback.CallCount);
    }

    private sealed class StubSelectionReader : ISelectionReader
    {
        private readonly string? _text;

        public StubSelectionReader(string? text)
        {
            _text = text;
        }

        public int CallCount { get; private set; }

        public Task<string?> TryReadSelectedTextAsync(ScreenPoint point, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_text);
        }
    }

    private sealed class BlockingSelectionReader : ISelectionReader
    {
        public async Task<string?> TryReadSelectedTextAsync(ScreenPoint point, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return null;
        }
    }
}
