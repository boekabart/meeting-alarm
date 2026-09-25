global using static Taal;
using System.Globalization;

/// <summary>
/// Alle zichtbare teksten. Elke eigenschap is <c>required</c>: een ontbrekende vertaling is een compileerfout.
/// Opmaak met <see cref="Taal.F"/>: {n:t} = korte tijd in de gekozen cultuur (en-US "2:03 PM", en-GB/nl/sv "14:03").
/// </summary>
sealed record Teksten
{
    // Tray-menu
    public required string MenuKomende { get; init; }
    public required string MenuVerversen { get; init; }
    public required string MenuTestPopup { get; init; }
    public required string MenuChatPopup { get; init; }
    public required string MenuAutostart { get; init; }
    public required string MenuInstellingen { get; init; }
    public required string MenuAfsluiten { get; init; }
    public required string MenuInloggenBij { get; init; }        // {0} naam

    // Statusregels in het menu: {0} naam, {1} tijd
    public required string StatusNogNiet { get; init; }
    public required string StatusGeenIcs { get; init; }
    public required string StatusAgendaOk { get; init; }         // {2} aantal meetings
    public required string StatusOk { get; init; }
    public required string StatusFout { get; init; }             // {2} melding
    public required string StatusInloggenVereist { get; init; }
    public required string StatusIngelogd { get; init; }

    // Ballonmeldingen
    public required string BalConfigFout { get; init; }          // {0} melding
    public required string BalHerladen { get; init; }
    public required string BalAgendaMislukt { get; init; }       // {0} naam, {1} melding
    public required string BalChatsMislukt { get; init; }        // {0} naam, {1} melding
    public required string BalOpnieuwInloggen { get; init; }     // {0} naam
    public required string BalInloggenMislukt { get; init; }     // {0} naam, {1} melding

    // Vensters en popups
    public required string KomendeTitel { get; init; }
    public required string GeenMeetings { get; init; }
    public required string TestMeeting { get; init; }
    public required string VoorbeeldCollega { get; init; }
    public required string Deelnemen { get; init; }
    public required string Straks { get; init; }
    public required string Sluiten { get; init; }
    public required string StartOver { get; init; }              // {0} min, {1} sec, {2} starttijd
    public required string NuBegonnen { get; init; }
    public required string BegonnenGeleden { get; init; }        // {0} min
    public required string ChatKop { get; init; }                // {0} aantal
    public required string AndereChats { get; init; }            // {0} aantal
    public required string AllesGezien { get; init; }
    public required string GezienTip { get; init; }

    // Opstarten
    public required string DraaitAl { get; init; }
    public required string AutostartVraag { get; init; }
    public required string ConfigAangemaakt { get; init; }       // {0} pad
    public required string ConfigFoutStart { get; init; }        // {0} pad, {1} melding

    // Gegevens
    public required string GeenTitel { get; init; }
    public required string OnbekendeChat { get; init; }
    public required string TeVeelVerzoeken { get; init; }        // {0} tijd
}

static class Taal
{
    static readonly Teksten EnUs = new()
    {
        MenuKomende = "Upcoming meetings…",
        MenuVerversen = "Refresh now",
        MenuTestPopup = "Show test popup",
        MenuChatPopup = "Show chat popup",
        MenuAutostart = "Start with Windows",
        MenuInstellingen = "Open settings",
        MenuAfsluiten = "Exit",
        MenuInloggenBij = "Sign in to {0}…",
        StatusNogNiet = "{0}: not fetched yet",
        StatusGeenIcs = "{0}: no ICS link filled in yet",
        StatusAgendaOk = "{0}: OK at {1:t}, {2} upcoming meeting(s)",
        StatusOk = "{0}: OK at {1:t}",
        StatusFout = "{0}: ERROR at {1:t} - {2}",
        StatusInloggenVereist = "{0}: sign-in required",
        StatusIngelogd = "{0}: signed in, fetching…",
        BalConfigFout = "Error in config.json, keeping the previous settings:\n{0}",
        BalHerladen = "Settings reloaded.",
        BalAgendaMislukt = "Fetching calendar '{0}' failed:\n{1}",
        BalChatsMislukt = "Fetching Teams chats '{0}' failed:\n{1}",
        BalOpnieuwInloggen = "Teams '{0}': please sign in again (right-click the tray icon).",
        BalInloggenMislukt = "Signing in to '{0}' failed:\n{1}",
        KomendeTitel = "Upcoming meetings",
        GeenMeetings = "No meetings found in the next 2 days.",
        TestMeeting = "Test meeting: weekly kickoff with the team",
        VoorbeeldCollega = "Example Colleague",
        Deelnemen = "Join",
        Straks = "Later",
        Sluiten = "Close",
        StartOver = "Starts in {0}:{1:00}   ({2:t})",
        NuBegonnen = "STARTED NOW!",
        BegonnenGeleden = "Started {0} min ago",
        ChatKop = "Teams — {0} unread",
        AndereChats = "+ {0} more chats",
        AllesGezien = "Mark all as seen",
        GezienTip = "Seen (until new messages arrive)",
        DraaitAl = "Meeting Alarm is already running (see the system tray).",
        AutostartVraag = "Start Meeting Alarm automatically with Windows?",
        ConfigAangemaakt = "Settings file created:\n{0}\n\nFill in your ICS links, and for Teams chats your Tenant (e.g. company.com) " +
                           "and LoginHint (your work email). Save; changes are loaded automatically.",
        ConfigFoutStart = "Error in {0}:\n\n{1}",
        GeenTitel = "(no title)",
        OnbekendeChat = "(chat)",
        TeVeelVerzoeken = "too many requests, pausing until {0:T}",
    };

    // Brits Engels: vrijwel dezelfde teksten; het echte verschil zit in de tijdnotatie van de cultuur (14:03 i.p.v. 2:03 PM).
    static readonly Teksten EnGb = EnUs with
    {
        TestMeeting = "Test meeting: weekly kick-off with the team",
        ConfigAangemaakt = "Settings file created:\n{0}\n\nFill in your ICS links, and for Teams chats your Tenant (e.g. company.co.uk) " +
                           "and LoginHint (your work e-mail). Save; changes are loaded automatically.",
        DraaitAl = "Meeting Alarm is already running (see the notification area).",
    };

    static readonly Teksten NlNl = new()
    {
        MenuKomende = "Komende meetings…",
        MenuVerversen = "Nu verversen",
        MenuTestPopup = "Test-popup tonen",
        MenuChatPopup = "Chat-popup tonen",
        MenuAutostart = "Start met Windows",
        MenuInstellingen = "Instellingen openen",
        MenuAfsluiten = "Afsluiten",
        MenuInloggenBij = "Inloggen bij {0}…",
        StatusNogNiet = "{0}: nog niet opgehaald",
        StatusGeenIcs = "{0}: nog geen ICS-link ingevuld",
        StatusAgendaOk = "{0}: OK om {1:t}, {2} komende meeting(s)",
        StatusOk = "{0}: OK om {1:t}",
        StatusFout = "{0}: FOUT om {1:t} - {2}",
        StatusInloggenVereist = "{0}: inloggen vereist",
        StatusIngelogd = "{0}: ingelogd, ophalen…",
        BalConfigFout = "Fout in config.json, oude instellingen blijven actief:\n{0}",
        BalHerladen = "Instellingen opnieuw geladen.",
        BalAgendaMislukt = "Agenda '{0}' ophalen mislukt:\n{1}",
        BalChatsMislukt = "Teams-chats '{0}' ophalen mislukt:\n{1}",
        BalOpnieuwInloggen = "Teams '{0}': opnieuw inloggen vereist (rechtsklik op het icoon).",
        BalInloggenMislukt = "Inloggen bij '{0}' mislukt:\n{1}",
        KomendeTitel = "Komende meetings",
        GeenMeetings = "Geen meetings gevonden in de komende 2 dagen.",
        TestMeeting = "Testmeeting: weekstart met het team",
        VoorbeeldCollega = "Voorbeeld Collega",
        Deelnemen = "Deelnemen",
        Straks = "Straks",
        Sluiten = "Sluiten",
        StartOver = "Start over {0}:{1:00}   ({2:t})",
        NuBegonnen = "NU BEGONNEN!",
        BegonnenGeleden = "Begonnen, {0} min geleden",
        ChatKop = "Teams — {0} ongelezen",
        AndereChats = "+ {0} andere chats",
        AllesGezien = "Alles gezien",
        GezienTip = "Gezien (tot er nieuwe berichten komen)",
        DraaitAl = "Meeting Alarm draait al (zie systeemvak).",
        AutostartVraag = "Meeting Alarm automatisch starten met Windows?",
        ConfigAangemaakt = "Instellingenbestand aangemaakt:\n{0}\n\nVul je ICS-links in, en voor Teams-chats je Tenant (bv. bedrijf.nl) " +
                           "en LoginHint (je werk-e-mail). Sla op; wijzigingen worden automatisch geladen.",
        ConfigFoutStart = "Fout in {0}:\n\n{1}",
        GeenTitel = "(geen titel)",
        OnbekendeChat = "(chat)",
        TeVeelVerzoeken = "te veel verzoeken, pauze tot {0:T}",
    };

    static readonly Teksten SvSe = new()
    {
        MenuKomende = "Kommande möten…",
        MenuVerversen = "Uppdatera nu",
        MenuTestPopup = "Visa testpopup",
        MenuChatPopup = "Visa chattpopup",
        MenuAutostart = "Starta med Windows",
        MenuInstellingen = "Öppna inställningar",
        MenuAfsluiten = "Avsluta",
        MenuInloggenBij = "Logga in på {0}…",
        StatusNogNiet = "{0}: inte hämtad än",
        StatusGeenIcs = "{0}: ingen ICS-länk angiven än",
        StatusAgendaOk = "{0}: OK kl. {1:t}, {2} kommande möte(n)",
        StatusOk = "{0}: OK kl. {1:t}",
        StatusFout = "{0}: FEL kl. {1:t} - {2}",
        StatusInloggenVereist = "{0}: inloggning krävs",
        StatusIngelogd = "{0}: inloggad, hämtar…",
        BalConfigFout = "Fel i config.json, de tidigare inställningarna behålls:\n{0}",
        BalHerladen = "Inställningarna har lästs in igen.",
        BalAgendaMislukt = "Det gick inte att hämta kalendern '{0}':\n{1}",
        BalChatsMislukt = "Det gick inte att hämta Teams-chattarna '{0}':\n{1}",
        BalOpnieuwInloggen = "Teams '{0}': logga in igen (högerklicka på ikonen).",
        BalInloggenMislukt = "Inloggningen på '{0}' misslyckades:\n{1}",
        KomendeTitel = "Kommande möten",
        GeenMeetings = "Inga möten hittades de kommande 2 dagarna.",
        TestMeeting = "Testmöte: veckostart med teamet",
        VoorbeeldCollega = "Exempel Kollega",
        Deelnemen = "Anslut",
        Straks = "Senare",
        Sluiten = "Stäng",
        StartOver = "Börjar om {0}:{1:00}   ({2:t})",
        NuBegonnen = "BÖRJAR NU!",
        BegonnenGeleden = "Började för {0} min sedan",
        ChatKop = "Teams — {0} olästa",
        AndereChats = "+ {0} chattar till",
        AllesGezien = "Markera alla som sedda",
        GezienTip = "Sedd (tills nya meddelanden kommer)",
        DraaitAl = "Meeting Alarm körs redan (se meddelandefältet).",
        AutostartVraag = "Starta Meeting Alarm automatiskt med Windows?",
        ConfigAangemaakt = "Inställningsfilen har skapats:\n{0}\n\nFyll i dina ICS-länkar, och för Teams-chattar din Tenant (t.ex. foretag.se) " +
                           "och LoginHint (din jobbmejl). Spara; ändringar läses in automatiskt.",
        ConfigFoutStart = "Fel i {0}:\n\n{1}",
        GeenTitel = "(ingen rubrik)",
        OnbekendeChat = "(chatt)",
        TeVeelVerzoeken = "för många förfrågningar, pausar till {0:T}",
    };

    internal static readonly IReadOnlyDictionary<string, Teksten> Alle = new Dictionary<string, Teksten>(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = EnUs,
        ["en-GB"] = EnGb,
        ["nl-NL"] = NlNl,
        ["sv-SE"] = SvSe,
    };

    public static Teksten T { get; private set; } = EnUs;
    public static CultureInfo Cultuur { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Kies de taal: "nl-NL", "sv", "en-GB", … of leeg voor de Windows-weergavetaal.
    /// Exacte match, anders dezelfde taal (nl-BE → nl-NL, en-AU → en-GB), anders en-US.
    /// </summary>
    public static void Kies(string? taal) => (Cultuur, T) = Zoek(taal, CultureInfo.CurrentUICulture);

    internal static (CultureInfo, Teksten) Zoek(string? taal, CultureInfo systeem)
    {
        var gevraagd = systeem;
        if (!string.IsNullOrWhiteSpace(taal))
        {
            try { gevraagd = CultureInfo.GetCultureInfo(taal.Trim()); }
            catch (CultureNotFoundException) { }
        }

        var naam = Alle.ContainsKey(gevraagd.Name) ? gevraagd.Name
            : gevraagd.TwoLetterISOLanguageName switch
            {
                "nl" => "nl-NL",
                "sv" => "sv-SE",
                "en" => gevraagd.Name is "en-US" or "en-CA" or "en-PH" or "en-029" || gevraagd.Name == "en" ? "en-US" : "en-GB",
                _ => "en-US",
            };
        return (CultureInfo.GetCultureInfo(naam), Alle[naam]);
    }

    /// <summary>Opmaak in de gekozen cultuur (tijden, getallen).</summary>
    public static string F(string opmaak, params object?[] args) => string.Format(Cultuur, opmaak, args);
}
