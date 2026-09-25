using System.Globalization;
using System.Text.RegularExpressions;

public class TaalTests
{
    static readonly Teksten EnUs = Taal.Alle["en-US"];

    static IEnumerable<(string Naam, string Waarde)> Eigenschappen(Teksten t) =>
        typeof(Teksten).GetProperties().Where(p => p.PropertyType == typeof(string))
            .Select(p => (p.Name, (string)p.GetValue(t)!));

    static string Plaatshouders(string s) =>
        string.Join(",", Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order());

    public static TheoryData<string> Talen => new(Taal.Alle.Keys);

    [Theory]
    [MemberData(nameof(Talen))]
    public void Elke_vertaling_gebruikt_dezelfde_plaatshouders_als_en_US(string taal)
    {
        var fout = Eigenschappen(Taal.Alle[taal]).Zip(Eigenschappen(EnUs))
            .Where(x => Plaatshouders(x.First.Waarde) != Plaatshouders(x.Second.Waarde))
            .Select(x => x.First.Naam);

        Assert.Empty(fout);
    }

    [Theory]
    [MemberData(nameof(Talen))]
    public void Geen_lege_teksten(string taal) =>
        Assert.Empty(Eigenschappen(Taal.Alle[taal]).Where(x => string.IsNullOrWhiteSpace(x.Waarde)).Select(x => x.Naam));

    [Theory]
    [InlineData("nl-NL", "en-US", "nl-NL")]
    [InlineData("nl", "en-US", "nl-NL")]
    [InlineData("nl-BE", "en-US", "nl-NL")]
    [InlineData("sv", "en-US", "sv-SE")]
    [InlineData("sv-FI", "en-US", "sv-SE")]
    [InlineData("en-GB", "nl-NL", "en-GB")]
    [InlineData("en-AU", "nl-NL", "en-GB")]
    [InlineData("en", "nl-NL", "en-US")]
    [InlineData("de-DE", "nl-NL", "en-US")]     // niet vertaald: Engels
    [InlineData("onzin-taal", "sv-SE", "sv-SE")] // ongeldig: Windows-taal
    [InlineData(null, "nl-NL", "nl-NL")]
    [InlineData("", "fr-FR", "en-US")]
    public void Taalkeuze(string? gevraagd, string systeem, string verwacht)
    {
        var (cultuur, teksten) = Taal.Zoek(gevraagd, CultureInfo.GetCultureInfo(systeem));

        Assert.Equal(verwacht, cultuur.Name);
        Assert.Same(Taal.Alle[verwacht], teksten);
    }

    [Fact]
    public void US_en_UK_verschillen_in_tijdnotatie()
    {
        var t = new DateTime(2026, 9, 25, 14, 3, 0);

        Assert.Contains("2:03", string.Format(CultureInfo.GetCultureInfo("en-US"), EnUs.StatusOk, "X", t));
        Assert.Contains("14:03", string.Format(CultureInfo.GetCultureInfo("en-GB"), Taal.Alle["en-GB"].StatusOk, "X", t));
    }
}
