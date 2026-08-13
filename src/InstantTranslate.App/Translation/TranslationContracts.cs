using InstantTranslate.Settings;

namespace InstantTranslate.Translation;

internal sealed record TranslationRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage);

internal sealed record TranslationChunk(string TextDelta, bool IsFinal = false);

internal interface IStreamingTranslationProvider
{
    string Id { get; }

    IAsyncEnumerable<TranslationChunk> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contract reserved for a future OpenAI-compatible SSE/chat-completions provider.
/// Implementations must yield text deltas as they arrive and honor cancellation.
/// </summary>
internal interface IOpenAiCompatibleStreamingProvider : IStreamingTranslationProvider
{
    OpenAiCompatibleProviderOptions Options { get; }
}

internal interface IDeepSeekStreamingProvider : IOpenAiCompatibleStreamingProvider
{
}

internal interface ITranslationProviderFactory
{
    IStreamingTranslationProvider Create(AppSettings settings);
}

internal sealed record OpenAiCompatibleProviderOptions(
    Uri Endpoint,
    string Model,
    string ApiKey)
{
    public override string ToString()
    {
        return $"Endpoint = {Endpoint}, Model = {Model}, ApiKey = ***";
    }
}

internal sealed class TranslationProviderException : Exception
{
    public TranslationProviderException(string message)
        : base(message)
    {
    }

    public TranslationProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
