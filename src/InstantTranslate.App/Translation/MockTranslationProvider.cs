using System.Runtime.CompilerServices;

namespace InstantTranslate.Translation;

internal sealed class MockTranslationProvider : IStreamingTranslationProvider, IStreamingExplanationProvider
{
    public string Id => "mock";

    public async IAsyncEnumerable<TranslationChunk> TranslateAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        cancellationToken.ThrowIfCancellationRequested();

        var translated = request.Text.Trim() switch
        {
            "Hello world" => "你好，世界",
            "Good morning" => "早上好",
            _ => "这是模拟译文。",
        };

        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TranslationChunk(translated, IsFinal: true);
    }

    public async IAsyncEnumerable<TranslationChunk> ExplainAsync(
        ExplanationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubjectText);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TranslationChunk(
            $"释义：{request.SubjectText.Trim()}。{Environment.NewLine}要点：这是模拟 AI 解释。{Environment.NewLine}语境：请结合原文和译文理解。",
            IsFinal: true);
    }
}
