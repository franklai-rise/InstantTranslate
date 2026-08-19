namespace InstantTranslate.Settings;

internal sealed record AppSettings
{
    public string UiLanguage { get; init; } = UiLanguageCatalog.DefaultLanguageId;

    public bool IsEnabled { get; init; } = true;

    public bool StartWithWindows { get; init; }

    /// <summary>
    /// Allows the last-resort WM_COPY reader for custom-rendered applications.
    /// It is intentionally disabled by default because WM_COPY temporarily
    /// changes the system clipboard while an automatic selection is read.
    /// </summary>
    public bool UseClipboardFallback { get; init; }

    public int SelectionDelayMilliseconds { get; init; } = 80;

    public int MaximumSelectionCharacters { get; init; } = 8000;

    public string SourceLanguage { get; init; } = "自动检测";

    public string TargetLanguage { get; init; } = "简体中文";

    public string TargetLanguageMode { get; init; } = "auto";

    /// <summary>
    /// When enabled, UI Automation may read the paragraph surrounding the
    /// selection and send it as non-translated context. It is disabled by
    /// default because the surrounding text can contain additional private data.
    /// </summary>
    public bool UseSelectionContext { get; init; }

    public string TranslationMode { get; init; } = TranslationPreferenceCatalog.DefaultModeId;

    public string TranslationTone { get; init; } = TranslationPreferenceCatalog.DefaultToneId;

    /// <summary>
    /// One source-to-target term pair per line, for example: API => API.
    /// This stores preferences only; source selections and translations are
    /// still never persisted by default.
    /// </summary>
    public string PersonalGlossary { get; init; } = string.Empty;

    public string ColorTheme { get; init; } = ThemeCatalog.DefaultThemeId;

    public string CustomAccentColor { get; init; } = ThemeCatalog.DefaultCustomAccent;

    public double DefaultTranslationFontSize { get; init; } = 16.5;

    public string EnglishTranslationFontFamily { get; init; } = TranslationFontCatalog.DefaultEnglishFontFamily;

    public string ChineseTranslationFontFamily { get; init; } = TranslationFontCatalog.DefaultChineseFontFamily;

    public string ProviderId { get; init; } = "deepseek";

    public string DeepSeekEndpoint { get; init; } = "https://api.deepseek.com";

    public string DeepSeekModel { get; init; } = "deepseek-v4-flash";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DeepSeekApiKey { get; init; } = string.Empty;

    public static AppSettings Default { get; } = new();
}
