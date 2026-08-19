using System.Text.Json;
using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class SettingsStoreMigrationTests
{
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
            PersonalGlossary = "  API => 接口  ",
            DeepSeekApiKey = "credential-secret",
        };

        var normalized = SettingsStore.NormalizeSettings(settings);

        Assert.Equal("en", normalized.UiLanguage);
        Assert.Equal("Times New Roman", normalized.EnglishTranslationFontFamily);
        Assert.Equal("SimHei", normalized.ChineseTranslationFontFamily);
        Assert.Equal("balanced", normalized.TranslationMode);
        Assert.Equal("natural", normalized.TranslationTone);
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
}
