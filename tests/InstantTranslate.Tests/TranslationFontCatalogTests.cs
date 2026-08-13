using InstantTranslate.Settings;

namespace InstantTranslate.Tests;

public sealed class TranslationFontCatalogTests
{
    [Fact]
    public void Catalogs_ContainRequiredCanonicalOptions()
    {
        Assert.Contains(TranslationFontCatalog.EnglishOptions, option => option.FamilyName == "Times New Roman");
        Assert.Contains(TranslationFontCatalog.EnglishOptions, option => option.FamilyName == "Arial");
        Assert.Contains(TranslationFontCatalog.EnglishOptions, option => option.FamilyName == "Source Sans Pro");
        Assert.Contains(TranslationFontCatalog.EnglishOptions, option => option.FamilyName == "Georgia");

        Assert.Contains(TranslationFontCatalog.ChineseOptions, option => option.FamilyName == "SimHei");
        Assert.Contains(TranslationFontCatalog.ChineseOptions, option => option.FamilyName == "Microsoft YaHei UI");
        Assert.Contains(TranslationFontCatalog.ChineseOptions, option => option.FamilyName == "SimSun");
        Assert.Contains(TranslationFontCatalog.ChineseOptions, option => option.FamilyName == "KaiTi");
    }

    [Theory]
    [InlineData(" times new roman ", "Times New Roman")]
    [InlineData("ARIAL", "Arial")]
    [InlineData("SourceSansPro", "Source Sans Pro")]
    [InlineData("not-installed-or-approved", "Times New Roman")]
    [InlineData(null, "Times New Roman")]
    public void NormalizeEnglish_ReturnsCanonicalWhitelistedValue(string? value, string expected)
    {
        Assert.Equal(expected, TranslationFontCatalog.NormalizeEnglish(value));
    }

    [Theory]
    [InlineData(" 黑体 ", "SimHei")]
    [InlineData("simhei", "SimHei")]
    [InlineData("微软雅黑", "Microsoft YaHei UI")]
    [InlineData("宋体", "SimSun")]
    [InlineData("楷体", "KaiTi")]
    [InlineData("untrusted-font", "SimHei")]
    [InlineData(null, "SimHei")]
    public void NormalizeChinese_ReturnsCanonicalWhitelistedValue(string? value, string expected)
    {
        Assert.Equal(expected, TranslationFontCatalog.NormalizeChinese(value));
    }
}
