namespace InstantTranslate.Settings;

internal sealed record AppSettings
{
    public bool IsEnabled { get; init; } = true;

    public int SelectionDelayMilliseconds { get; init; } = 80;

    public string SourceLanguage { get; init; } = "自动检测";

    public string TargetLanguage { get; init; } = "简体中文";

    public string TargetLanguageMode { get; init; } = "auto";

    public string ColorTheme { get; init; } = ThemeCatalog.DefaultThemeId;

    public string CustomAccentColor { get; init; } = ThemeCatalog.DefaultCustomAccent;

    public string ProviderId { get; init; } = "deepseek";

    public string DeepSeekEndpoint { get; init; } = "https://api.deepseek.com";

    public string DeepSeekModel { get; init; } = "deepseek-v4-flash";

    [System.Text.Json.Serialization.JsonIgnore]
    public string DeepSeekApiKey { get; init; } = string.Empty;

    public static AppSettings Default { get; } = new();
}
