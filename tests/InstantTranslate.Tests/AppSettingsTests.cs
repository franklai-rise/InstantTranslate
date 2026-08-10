using System.Text.Json;
using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Serialization_DoesNotContainApiKey()
    {
        var settings = AppSettings.Default with { DeepSeekApiKey = "secret-value" };

        var json = JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("secret-value", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DeepSeekApiKey", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OldSettingsJson_DefaultsToOceanTheme()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}");

        Assert.NotNull(settings);
        Assert.Equal(ThemeCatalog.DefaultThemeId, settings.ColorTheme);
        Assert.Equal(ThemeCatalog.DefaultCustomAccent, settings.CustomAccentColor);
    }
}
