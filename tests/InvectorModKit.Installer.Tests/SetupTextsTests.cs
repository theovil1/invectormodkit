using System.Text.RegularExpressions;
using InvectorModKit.Installer;

namespace InvectorModKit.Installer.Tests;

public class SetupTextsTests
{
    [Fact]
    public void EveryText_InEveryLanguage_WithTheSamePlaceholders()
    {
        Assert.Equal(11, SetupTexts.Languages.Length);
        foreach (string key in SetupTexts.Keys)
        {
            string english = SetupTexts.Get("en", key);
            var holes = Regex.Matches(english, @"\{\d\}").Select(m => m.Value).OrderBy(s => s).ToList();
            foreach (string lang in SetupTexts.Languages)
            {
                string text = SetupTexts.Get(lang, key);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{lang}.{key}");
                Assert.Equal(holes, Regex.Matches(text, @"\{\d\}").Select(m => m.Value).OrderBy(s => s).ToList());
            }
        }
    }

    [Theory]
    [InlineData("fr-FR", "fr")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("zh-Hans-CN", "zh")]
    [InlineData("ja-JP", "ja")]
    [InlineData("nl-NL", "en")]
    [InlineData("", "en")]
    public void Resolve_WindowsCulture(string culture, string expected) => Assert.Equal(expected, SetupTexts.Resolve(culture));

    [Fact]
    public void Get_FillsPlaceholders() => Assert.Equal("Mettre à jour vers 0.2.0", SetupTexts.Get("fr", "update", "0.2.0"));
}
