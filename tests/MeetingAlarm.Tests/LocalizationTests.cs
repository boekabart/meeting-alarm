using System.Globalization;
using System.Text.RegularExpressions;

public class LocalizationTests
{
    static readonly Texts EnUs = Loc.All["en-US"];

    static IEnumerable<(string Name, string Value)> Properties(Texts t) =>
        typeof(Texts).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => (p.Name, (string)p.GetValue(t)!));

    static string Placeholders(string s) =>
        string.Join(",", Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order());

    public static TheoryData<string> Languages => new(Loc.All.Keys);

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_translation_uses_the_same_placeholders_as_en_US(string language)
    {
        var wrong = Properties(Loc.All[language]).Zip(Properties(EnUs))
            .Where(x => Placeholders(x.First.Value) != Placeholders(x.Second.Value))
            .Select(x => x.First.Name);

        Assert.Empty(wrong);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_empty_texts(string language) =>
        Assert.Empty(Properties(Loc.All[language]).Where(x => string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Name));

    [Theory]
    [InlineData("nl-NL", "en-US", "nl-NL")]
    [InlineData("nl", "en-US", "nl-NL")]
    [InlineData("nl-BE", "en-US", "nl-NL")]
    [InlineData("sv", "en-US", "sv-SE")]
    [InlineData("sv-FI", "en-US", "sv-SE")]
    [InlineData("en-GB", "nl-NL", "en-GB")]
    [InlineData("en-AU", "nl-NL", "en-GB")]
    [InlineData("en", "nl-NL", "en-US")]
    [InlineData("de-DE", "nl-NL", "en-US")]        // not translated: English
    [InlineData("not-a-language", "sv-SE", "sv-SE")] // invalid: Windows language
    [InlineData(null, "nl-NL", "nl-NL")]
    [InlineData("", "fr-FR", "en-US")]
    public void Language_choice(string? requested, string system, string expected)
    {
        var (culture, texts) = Loc.Resolve(requested, CultureInfo.GetCultureInfo(system));

        Assert.Equal(expected, culture.Name);
        Assert.Same(Loc.All[expected], texts);
    }

    [Fact]
    public void US_and_UK_differ_in_time_format()
    {
        var t = new DateTime(2026, 9, 25, 14, 3, 0);

        Assert.Contains("2:03", string.Format(CultureInfo.GetCultureInfo("en-US"), EnUs.StatusOk, "X", t));
        Assert.Contains("14:03", string.Format(CultureInfo.GetCultureInfo("en-GB"), Loc.All["en-GB"].StatusOk, "X", t));
    }
}
