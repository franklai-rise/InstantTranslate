using InstantTranslate.Settings;

namespace InstantTranslate.Translation;

internal sealed record TranslationRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string? Context = null,
    string Mode = TranslationPreferenceCatalog.DefaultModeId,
    string Tone = TranslationPreferenceCatalog.DefaultToneId,
    string PersonalGlossary = "",
    IReadOnlyList<GlossaryEntry>? ApplicableGlossaryEntries = null,
    IReadOnlyList<TranslationExample>? TranslationExamples = null);

internal sealed record TranslationExample(string SourceText, string TargetText);

internal sealed record TranslationChunk(string TextDelta, bool IsFinal = false);

/// <summary>
/// Context sent to the AI when the user asks for an explanation. The source and
/// full translation are reference-only; <see cref="SubjectText"/> is the text
/// that must be explained.
/// </summary>
internal sealed record ExplanationRequest(
    string SubjectText,
    string SourceText,
    string TranslationText,
    string SourceLanguage,
    string TargetLanguage,
    ExplanationScope Scope);

internal enum ExplanationScope
{
    SourceText,
    TranslationSelection,
}

internal interface IStreamingTranslationProvider
{
    string Id { get; }

    IAsyncEnumerable<TranslationChunk> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}

internal interface IStreamingExplanationProvider
{
    IAsyncEnumerable<TranslationChunk> ExplainAsync(
        ExplanationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Contract reserved for a future OpenAI-compatible SSE/chat-completions provider.
/// Implementations must yield text deltas as they arrive and honor cancellation.
/// </summary>
internal interface IOpenAiCompatibleStreamingProvider : IStreamingTranslationProvider, IStreamingExplanationProvider
{
    OpenAiCompatibleProviderOptions Options { get; }
}

internal interface IDeepSeekStreamingProvider : IOpenAiCompatibleStreamingProvider
{
}

internal interface ITranslationProviderFactory
{
    IStreamingTranslationProvider Create(AppSettings settings);

    IStreamingExplanationProvider CreateExplanationProvider(AppSettings settings);
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

internal enum TranslationFailureKind
{
    Unknown,
    Configuration,
    Authentication,
    InvalidRequest,
    Connectivity,
    Timeout,
    RateLimit,
    Server,
    Protocol,
}

internal sealed class TranslationProviderException : Exception
{
    public TranslationFailureKind Kind { get; }

    public bool IsTransient => Kind is TranslationFailureKind.Connectivity
        or TranslationFailureKind.Timeout
        or TranslationFailureKind.RateLimit
        or TranslationFailureKind.Server;

    public TranslationProviderException(string message)
        : this(message, TranslationFailureKind.Unknown)
    {
    }

    public TranslationProviderException(
        string message,
        TranslationFailureKind kind)
        : base(message)
    {
        Kind = kind;
    }

    public TranslationProviderException(string message, Exception innerException)
        : this(message, innerException, TranslationFailureKind.Unknown)
    {
    }

    public TranslationProviderException(
        string message,
        Exception innerException,
        TranslationFailureKind kind)
        : base(message, innerException)
    {
        Kind = kind;
    }
}
