using System.IO;
using System.Text.Json;

namespace InstantTranslate.Settings;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly WindowsCredentialStore _credentialStore;
    private readonly string _settingsPath;

    public SettingsStore(WindowsCredentialStore credentialStore, string? settingsPath = null)
    {
        _credentialStore = credentialStore;
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InstantTranslate",
            "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                    ?? AppSettings.Default
                : AppSettings.Default;
        }
        catch (JsonException)
        {
            settings = AppSettings.Default;
        }
        catch (IOException)
        {
            settings = AppSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            settings = AppSettings.Default;
        }

        settings = NormalizeSettings(settings);

        try
        {
            return settings with { DeepSeekApiKey = _credentialStore.ReadApiKey() };
        }
        catch
        {
            return settings with { DeepSeekApiKey = string.Empty };
        }
    }

    public void Save(AppSettings settings, bool persistApiKey = true)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("设置文件路径无效。");
        Directory.CreateDirectory(directory);

        var normalizedSettings = NormalizeSettings(settings);
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(
                normalizedSettings with { DeepSeekApiKey = string.Empty },
                JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (persistApiKey)
        {
            _credentialStore.SaveApiKey(settings.DeepSeekApiKey);
        }
    }

    internal static AppSettings NormalizeSettings(AppSettings? settings)
    {
        var value = settings ?? AppSettings.Default;
        return value with
        {
            UiLanguage = UiLanguageCatalog.Normalize(value.UiLanguage),
            EnglishTranslationFontFamily = TranslationFontCatalog.NormalizeEnglish(
                value.EnglishTranslationFontFamily),
            ChineseTranslationFontFamily = TranslationFontCatalog.NormalizeChinese(
                value.ChineseTranslationFontFamily),
        };
    }
}
