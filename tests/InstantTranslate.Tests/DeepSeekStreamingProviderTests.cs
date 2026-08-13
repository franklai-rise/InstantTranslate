using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using InstantTranslate.Translation;

namespace InstantTranslate.Tests;

public sealed class DeepSeekStreamingProviderTests
{
    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/chat/completions")]
    [InlineData("https://api.deepseek.com/v1/", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://example.test/chat/completions", "https://example.test/chat/completions")]
    public void BuildChatCompletionsUri_AppendsExpectedPath(string endpoint, string expected)
    {
        Assert.Equal(expected, DeepSeekStreamingProvider.BuildChatCompletionsUri(new Uri(endpoint)).ToString());
    }

    [Fact]
    public void ReadContentDelta_IgnoresReasoningAndReturnsContent()
    {
        const string json = """
            {"choices":[{"delta":{"reasoning_content":"hidden","content":"译文"}}]}
            """;

        var content = DeepSeekStreamingProvider.ReadContentDelta(json);

        Assert.Equal("译文", content);
    }

    [Fact]
    public void ProviderOptions_ToString_DoesNotExposeApiKey()
    {
        var options = new OpenAiCompatibleProviderOptions(
            new Uri("https://api.deepseek.com"),
            "deepseek-v4-flash",
            "highly-secret-key");

        var display = options.ToString();

        Assert.DoesNotContain("highly-secret-key", display, StringComparison.Ordinal);
        Assert.Contains("deepseek-v4-flash", display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_SendsDeepSeekRequestAndCombinesSseContent()
    {
        const string sse = """
            data: {"choices":[{"delta":{"role":"assistant","content":"你"}}]}

            data: {"choices":[{"delta":{"content":"好"}}]}

            data: [DONE]

            """;
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        using var httpClient = new HttpClient(handler);
        var provider = CreateProvider(httpClient);
        var chunks = new List<TranslationChunk>();

        await foreach (var chunk in provider.TranslateAsync(
                           new TranslationRequest("Hello", "英语", "简体中文"),
                           CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal("你好", string.Concat(chunks.Select(chunk => chunk.TextDelta)));
        Assert.True(chunks[^1].IsFinal);
        Assert.Equal("https://api.deepseek.com/chat/completions", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-key", handler.AuthorizationParameter);

        using var requestDocument = JsonDocument.Parse(Assert.IsType<string>(handler.RequestBody));
        var root = requestDocument.RootElement;
        Assert.Equal("deepseek-v4-flash", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("Hello", root.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task TranslateAsync_OnApiError_ThrowsSafeProviderException()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Invalid API key\"}}", Encoding.UTF8, "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(async () =>
        {
            await foreach (var _ in provider.TranslateAsync(
                               new TranslationRequest("Hello", "英语", "简体中文"),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_RetriesOneTransientStatusBeforeContent()
    {
        var attempt = 0;
        var handler = new RecordingHandler(_ =>
        {
            attempt++;
            return attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "data: {\"choices\":[{\"delta\":{\"content\":\"成功\"}}]}\n\ndata: [DONE]\n\n",
                        Encoding.UTF8,
                        "text/event-stream"),
                };
        });
        using var httpClient = new HttpClient(handler);
        var provider = CreateProvider(httpClient);
        var text = new StringBuilder();

        await foreach (var chunk in provider.TranslateAsync(
                           new TranslationRequest("Hello", "英语", "简体中文"),
                           CancellationToken.None))
        {
            text.Append(chunk.TextDelta);
        }

        Assert.Equal("成功", text.ToString());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task TranslateAsync_OnSuccessfulJsonResponse_ReportsNonStreamingError()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"stream disabled\"}}",
                Encoding.UTF8,
                "application/json"),
        });
        using var httpClient = new HttpClient(handler);
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(async () =>
        {
            await foreach (var _ in provider.TranslateAsync(
                               new TranslationRequest("Hello", "英语", "简体中文"),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains("非流式响应", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stream disabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_OnStreamErrorPayload_ReportsApiMessage()
    {
        const string sse = "data: {\"error\":{\"message\":\"quota exhausted\"}}\n\n";
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        });
        using var httpClient = new HttpClient(handler);
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<TranslationProviderException>(async () =>
        {
            await foreach (var _ in provider.TranslateAsync(
                               new TranslationRequest("Hello", "英语", "简体中文"),
                               CancellationToken.None))
            {
            }
        });

        Assert.Contains("quota exhausted", exception.Message, StringComparison.Ordinal);
    }

    private static DeepSeekStreamingProvider CreateProvider(HttpClient httpClient)
    {
        return new DeepSeekStreamingProvider(
            httpClient,
            new OpenAiCompatibleProviderOptions(
                new Uri("https://api.deepseek.com"),
                "deepseek-v4-flash",
                "test-key"));
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string? RequestBody { get; private set; }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
