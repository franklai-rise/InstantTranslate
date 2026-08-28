using System.Text.Json;
using System.IO;
using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class SettingsStoreMigrationTests
{
    [Fact]
    public void LoadPreferences_MalformedJsonReportsFailureWithoutReadingCredential()
    {
        var path = Path.Combine(Path.GetTempPath(), $"InstantTranslate-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ malformed");
        try
        {
            var store = new SettingsStore(new NeverReadApiKeyStore(), path);

            var settings = store.LoadPreferences();

            Assert.True(store.SettingsReadFailed);
            Assert.Equal("deepseek", settings.ProviderId);
            Assert.Empty(settings.DeepSeekApiKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NormalizeSettings_OldJson_AddsNewDefaultsWithoutChangingExistingValues()
    {
        const string oldJson = """
            {
              "IsEnabled": false,
              "SelectionDelayMilliseconds": 125,
              "ProviderId": "mock"
            }
            """;
        var oldSettings = JsonSerializer.Deserialize<AppSettings>(oldJson);

        var migrated = SettingsStore.NormalizeSettings(oldSettings);

        Assert.False(migrated.IsEnabled);
        Assert.Equal(125, migrated.SelectionDelayMilliseconds);
        Assert.Equal("mock", migrated.ProviderId);
        Assert.False(migrated.UseClipboardFallback);
        Assert.Equal("en", migrated.UiLanguage);
        Assert.Equal("Times New Roman", migrated.EnglishTranslationFontFamily);
        Assert.Equal("SimHei", migrated.ChineseTranslationFontFamily);
        Assert.False(migrated.UseSelectionContext);
        Assert.Equal("balanced", migrated.TranslationMode);
        Assert.Equal("natural", migrated.TranslationTone);
        Assert.Equal(PopupVisualStyleCatalog.DefaultStyleId, migrated.PopupVisualStyle);
    }

    [Fact]
    public void NormalizeSettings_BadValues_FallBackAndLeaveApiKeyUntouched()
    {
        var settings = AppSettings.Default with
        {
            UiLanguage = "xx-invalid",
            EnglishTranslationFontFamily = "Unknown English Font",
            ChineseTranslationFontFamily = "Unknown Chinese Font",
            TranslationMode = "turbo",
            TranslationTone = "dramatic",
            PopupVisualStyle = "unrecognized-style",
            PersonalGlossary = "  API => 接口  ",
            DeepSeekApiKey = "credential-secret",
        };

        var normalized = SettingsStore.NormalizeSettings(settings);

        Assert.Equal("en", normalized.UiLanguage);
        Assert.Equal("Times New Roman", normalized.EnglishTranslationFontFamily);
        Assert.Equal("SimHei", normalized.ChineseTranslationFontFamily);
        Assert.Equal("balanced", normalized.TranslationMode);
        Assert.Equal("natural", normalized.TranslationTone);
        Assert.Equal(PopupVisualStyleCatalog.DefaultStyleId, normalized.PopupVisualStyle);
        Assert.Equal("API => 接口", normalized.PersonalGlossary);
        Assert.Equal("credential-secret", normalized.DeepSeekApiKey);
    }

    [Fact]
    public void NormalizeSettings_Aliases_AreMigratedToCanonicalStoredNames()
    {
        var settings = AppSettings.Default with
        {
            UiLanguage = "zh",
            EnglishTranslationFontFamily = "SourceSansPro",
            ChineseTranslationFontFamily = "黑体",
        };

        var normalized = SettingsStore.NormalizeSettings(settings);

        Assert.Equal("zh-CN", normalized.UiLanguage);
        Assert.Equal("Source Sans Pro", normalized.EnglishTranslationFontFamily);
        Assert.Equal("SimHei", normalized.ChineseTranslationFontFamily);
    }

    [Fact]
    public void NormalizeSettings_NullProviderValues_FallBackToSafeDefaults()
    {
        var settings = AppSettings.Default with
        {
            ProviderId = null!,
            DeepSeekEndpoint = null!,
            DeepSeekModel = null!,
            SourceLanguage = null!,
            TargetLanguage = null!,
            TargetLanguageMode = null!,
        };

        var normalized = SettingsStore.NormalizeSettings(settings);

        Assert.Equal("deepseek", normalized.ProviderId);
        Assert.Equal("https://api.deepseek.com", normalized.DeepSeekEndpoint);
        Assert.Equal("deepseek-v4-flash", normalized.DeepSeekModel);
        Assert.Equal("自动检测", normalized.SourceLanguage);
        Assert.Equal("简体中文", normalized.TargetLanguage);
        Assert.Equal("auto", normalized.TargetLanguageMode);
    }

    private sealed class NeverReadApiKeyStore : IApiKeyStore
    {
        public string ReadApiKey() => throw new InvalidOperationException("Must not be called.");

        public void SaveApiKey(string apiKey) => throw new InvalidOperationException("Must not be called.");
    }
}
