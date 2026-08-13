using System.Net;
using System.Net.Http;
using System.Reflection;
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
            // The coordinator owns cancellation and the user-facing timeout.
            // A second HttpClient timeout used to race that policy and could leave
            // the popup permanently displaying its loading state.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"InstantTranslate/{version?.ToString(3) ?? "0.0.0"}");
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
        if (!TryValidateEndpoint(settings.DeepSeekEndpoint, out var endpoint))
        {
            throw new TranslationProviderException("DeepSeek Endpoint 无效；远程地址必须使用 HTTPS。");
        }

        if (string.IsNullOrWhiteSpace(settings.DeepSeekModel))
        {
            throw new TranslationProviderException("尚未配置 DeepSeek Model，请从托盘打开设置。");
        }

        return new DeepSeekStreamingProvider(
            _httpClient,
            new OpenAiCompatibleProviderOptions(
                endpoint!,
                settings.DeepSeekModel,
                settings.DeepSeekApiKey));
    }

    internal static bool TryValidateEndpoint(string? value, out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate)
            || candidate.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment)
            || !string.IsNullOrEmpty(candidate.Query))
        {
            return false;
        }

        if (candidate.Scheme == Uri.UriSchemeHttp && !candidate.IsLoopback)
        {
            return false;
        }

        endpoint = candidate;
        return true;
    }

    public void Dispose() => _httpClient.Dispose();
}
