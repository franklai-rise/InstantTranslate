using System.Net;
using System.Net.Http;
using InstantTranslate.Settings;

namespace InstantTranslate.Translation;

internal sealed class TranslationProviderFactory : ITranslationProviderFactory, IDisposable
{
    private readonly MockTranslationProvider _mockProvider = new();
    private readonly HttpClient _httpClient;

    public TranslationProviderFactory()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("InstantTranslate/0.1");
    }

    public IStreamingTranslationProvider Create(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.ProviderId.ToLowerInvariant() switch
        {
            "mock" => _mockProvider,
            "deepseek" => CreateDeepSeekProvider(settings),
            _ => throw new TranslationProviderException($"未知翻译 Provider：{settings.ProviderId}"),
        };
    }

    private DeepSeekStreamingProvider CreateDeepSeekProvider(AppSettings settings)
    {
        if (!Uri.TryCreate(settings.DeepSeekEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new TranslationProviderException("DeepSeek Endpoint 无效，请从托盘打开设置。");
        }

        if (string.IsNullOrWhiteSpace(settings.DeepSeekModel))
        {
            throw new TranslationProviderException("尚未配置 DeepSeek Model，请从托盘打开设置。");
        }

        return new DeepSeekStreamingProvider(
            _httpClient,
            new OpenAiCompatibleProviderOptions(
                endpoint,
                settings.DeepSeekModel,
                settings.DeepSeekApiKey));
    }

    public void Dispose() => _httpClient.Dispose();
}
