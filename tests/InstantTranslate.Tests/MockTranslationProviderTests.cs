using InstantTranslate.Translation;

namespace InstantTranslate.Tests;

public sealed class MockTranslationProviderTests
{
    [Theory]
    [InlineData("Hello world", "你好，世界")]
    [InlineData("Good morning", "早上好")]
    [InlineData("Something else", "这是模拟译文。")]
    public async Task TranslateAsync_ReturnsDeterministicResult(string source, string expected)
    {
        var provider = new MockTranslationProvider();
        var chunks = new List<TranslationChunk>();

        await foreach (var chunk in provider.TranslateAsync(
                           new TranslationRequest(source, "自动检测", "简体中文"),
                           CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        var resultChunk = Assert.Single(chunks);
        Assert.Equal(expected, resultChunk.TextDelta);
        Assert.True(resultChunk.IsFinal);
    }
}
