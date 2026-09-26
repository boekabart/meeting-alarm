global using static Loc;
using System.Globalization;

/// <summary>
/// All visible texts. Every property is <c>required</c>: a missing translation is a compile error.
/// Formatting via <see cref="Loc.F"/>: {n:t} = short time in the chosen culture (en-US "2:03 PM", en-GB/nl/sv "14:03").
/// </summary>
sealed record Texts
{
    // Tray menu
    public required string MenuUpcoming { get; init; }
    public required string MenuRefresh { get; init; }
    public required string MenuTestPopup { get; init; }
    public required string MenuChatPopup { get; init; }
    public required string MenuAutostart { get; init; }
    public required string MenuSettings { get; init; }
    public required string MenuExit { get; init; }
    public required string MenuSignInTo { get; init; }           // {0} name

    // Status lines in the menu: {0} name, {1} time
    public required string StatusNotFetched { get; init; }
    public required string StatusNoIcsLink { get; init; }
    public required string StatusCalendarOk { get; init; }       // {2} number of meetings
    public required string StatusOk { get; init; }
    public required string StatusError { get; init; }            // {2} message
    public required string StatusSignInRequired { get; init; }
    public required string StatusSignedIn { get; init; }

    // Balloon notifications
    public required string BalloonConfigError { get; init; }     // {0} message
    public required string BalloonReloaded { get; init; }
    public required string BalloonCalendarFailed { get; init; }  // {0} name, {1} message
    public required string BalloonChatsFailed { get; init; }     // {0} name, {1} message
    public required string BalloonSignInAgain { get; init; }     // {0} name
    public required string BalloonSignInFailed { get; init; }    // {0} name, {1} message

    // Windows and popups
    public required string UpcomingTitle { get; init; }
    public required string NoMeetings { get; init; }
    public required string ExampleMeeting { get; init; }
    public required string ExampleColleague { get; init; }
    public required string Join { get; init; }
    public required string Later { get; init; }
    public required string CloseButton { get; init; }
    public required string StartsIn { get; init; }               // {0} min, {1} sec, {2} start time
    public required string StartedNow { get; init; }
    public required string StartedAgo { get; init; }             // {0} min
    public required string ChatHeader { get; init; }             // {0} count
    public required string MoreChats { get; init; }              // {0} count
    public required string MarkAllSeen { get; init; }
    public required string SeenTooltip { get; init; }

    // Startup
    public required string AlreadyRunning { get; init; }
    public required string AutostartQuestion { get; init; }
    public required string ConfigCreated { get; init; }          // {0} path
    public required string ConfigErrorAtStart { get; init; }     // {0} path, {1} message

    // Data
    public required string NoTitle { get; init; }
    public required string UnknownChat { get; init; }
    public required string TooManyRequests { get; init; }        // {0} time
}

static class Loc
{
    static readonly Texts EnUs = new()
    {
        MenuUpcoming = "Upcoming meetings…",
        MenuRefresh = "Refresh now",
        MenuTestPopup = "Show test popup",
        MenuChatPopup = "Show chat popup",
        MenuAutostart = "Start with Windows",
        MenuSettings = "Open settings",
        MenuExit = "Exit",
        MenuSignInTo = "Sign in to {0}…",
        StatusNotFetched = "{0}: not fetched yet",
        StatusNoIcsLink = "{0}: no ICS link filled in yet",
        StatusCalendarOk = "{0}: OK at {1:t}, {2} upcoming meeting(s)",
        StatusOk = "{0}: OK at {1:t}",
        StatusError = "{0}: ERROR at {1:t} - {2}",
        StatusSignInRequired = "{0}: sign-in required",
        StatusSignedIn = "{0}: signed in, fetching…",
        BalloonConfigError = "Error in config.json, keeping the previous settings:\n{0}",
        BalloonReloaded = "Settings reloaded.",
        BalloonCalendarFailed = "Fetching calendar '{0}' failed:\n{1}",
        BalloonChatsFailed = "Fetching Teams chats '{0}' failed:\n{1}",
        BalloonSignInAgain = "Teams '{0}': please sign in again (right-click the tray icon).",
        BalloonSignInFailed = "Signing in to '{0}' failed:\n{1}",
        UpcomingTitle = "Upcoming meetings",
        NoMeetings = "No meetings found in the next 2 days.",
        ExampleMeeting = "Example: this is what a meeting alert looks like",
        ExampleColleague = "Example Colleague",
        Join = "Join",
        Later = "Later",
        CloseButton = "Close",
        StartsIn = "Starts in {0}:{1:00}   ({2:t})",
        StartedNow = "STARTED NOW!",
        StartedAgo = "Started {0} min ago",
        ChatHeader = "Teams — {0} unread",
        MoreChats = "+ {0} more chats",
        MarkAllSeen = "Mark all as seen",
        SeenTooltip = "Seen (until new messages arrive)",
        AlreadyRunning = "Meeting Alarm is already running (see the system tray).",
        AutostartQuestion = "Start Meeting Alarm automatically with Windows?",
        ConfigCreated = "Settings file created:\n{0}\n\nFill in each job's Tenant (e.g. company.com) and LoginHint (your work email). " +
                        "Calendars from elsewhere can be added as ICS links. Save; changes are loaded automatically.",
        ConfigErrorAtStart = "Error in {0}:\n\n{1}",
        NoTitle = "(no title)",
        UnknownChat = "(chat)",
        TooManyRequests = "too many requests, pausing until {0:T}",
    };

    // British English: almost the same texts; the real difference is the culture's time format (14:03 instead of 2:03 PM).
    static readonly Texts EnGb = EnUs with
    {
        ConfigCreated = "Settings file created:\n{0}\n\nFill in each job's Tenant (e.g. company.co.uk) and LoginHint (your work e-mail). " +
                        "Calendars from elsewhere can be added as ICS links. Save; changes are loaded automatically.",
        AlreadyRunning = "Meeting Alarm is already running (see the notification area).",
    };

    static readonly Texts NlNl = new()
    {
        MenuUpcoming = "Komende meetings…",
        MenuRefresh = "Nu verversen",
        MenuTestPopup = "Test-popup tonen",
        MenuChatPopup = "Chat-popup tonen",
        MenuAutostart = "Start met Windows",
        MenuSettings = "Instellingen openen",
        MenuExit = "Afsluiten",
        MenuSignInTo = "Inloggen bij {0}…",
        StatusNotFetched = "{0}: nog niet opgehaald",
        StatusNoIcsLink = "{0}: nog geen ICS-link ingevuld",
        StatusCalendarOk = "{0}: OK om {1:t}, {2} komende meeting(s)",
        StatusOk = "{0}: OK om {1:t}",
        StatusError = "{0}: FOUT om {1:t} - {2}",
        StatusSignInRequired = "{0}: inloggen vereist",
        StatusSignedIn = "{0}: ingelogd, ophalen…",
        BalloonConfigError = "Fout in config.json, oude instellingen blijven actief:\n{0}",
        BalloonReloaded = "Instellingen opnieuw geladen.",
        BalloonCalendarFailed = "Agenda '{0}' ophalen mislukt:\n{1}",
        BalloonChatsFailed = "Teams-chats '{0}' ophalen mislukt:\n{1}",
        BalloonSignInAgain = "Teams '{0}': opnieuw inloggen vereist (rechtsklik op het icoon).",
        BalloonSignInFailed = "Inloggen bij '{0}' mislukt:\n{1}",
        UpcomingTitle = "Komende meetings",
        NoMeetings = "Geen meetings gevonden in de komende 2 dagen.",
        ExampleMeeting = "Voorbeeld: zo ziet een meeting-melding eruit",
        ExampleColleague = "Voorbeeld Collega",
        Join = "Deelnemen",
        Later = "Straks",
        CloseButton = "Sluiten",
        StartsIn = "Start over {0}:{1:00}   ({2:t})",
        StartedNow = "NU BEGONNEN!",
        StartedAgo = "Begonnen, {0} min geleden",
        ChatHeader = "Teams — {0} ongelezen",
        MoreChats = "+ {0} andere chats",
        MarkAllSeen = "Alles gezien",
        SeenTooltip = "Gezien (tot er nieuwe berichten komen)",
        AlreadyRunning = "Meeting Alarm draait al (zie systeemvak).",
        AutostartQuestion = "Meeting Alarm automatisch starten met Windows?",
        ConfigCreated = "Instellingenbestand aangemaakt:\n{0}\n\nVul per job de Tenant (bv. bedrijf.nl) en LoginHint (je werk-e-mail) in. " +
                        "Agenda's van elders kun je als ICS-link toevoegen. Sla op; wijzigingen worden automatisch geladen.",
        ConfigErrorAtStart = "Fout in {0}:\n\n{1}",
        NoTitle = "(geen titel)",
        UnknownChat = "(chat)",
        TooManyRequests = "te veel verzoeken, pauze tot {0:T}",
    };

    static readonly Texts SvSe = new()
    {
        MenuUpcoming = "Kommande möten…",
        MenuRefresh = "Uppdatera nu",
        MenuTestPopup = "Visa testpopup",
        MenuChatPopup = "Visa chattpopup",
        MenuAutostart = "Starta med Windows",
        MenuSettings = "Öppna inställningar",
        MenuExit = "Avsluta",
        MenuSignInTo = "Logga in på {0}…",
        StatusNotFetched = "{0}: inte hämtad än",
        StatusNoIcsLink = "{0}: ingen ICS-länk angiven än",
        StatusCalendarOk = "{0}: OK kl. {1:t}, {2} kommande möte(n)",
        StatusOk = "{0}: OK kl. {1:t}",
        StatusError = "{0}: FEL kl. {1:t} - {2}",
        StatusSignInRequired = "{0}: inloggning krävs",
        StatusSignedIn = "{0}: inloggad, hämtar…",
        BalloonConfigError = "Fel i config.json, de tidigare inställningarna behålls:\n{0}",
        BalloonReloaded = "Inställningarna har lästs in igen.",
        BalloonCalendarFailed = "Det gick inte att hämta kalendern '{0}':\n{1}",
        BalloonChatsFailed = "Det gick inte att hämta Teams-chattarna '{0}':\n{1}",
        BalloonSignInAgain = "Teams '{0}': logga in igen (högerklicka på ikonen).",
        BalloonSignInFailed = "Inloggningen på '{0}' misslyckades:\n{1}",
        UpcomingTitle = "Kommande möten",
        NoMeetings = "Inga möten hittades de kommande 2 dagarna.",
        ExampleMeeting = "Exempel: så här ser en mötesavisering ut",
        ExampleColleague = "Exempel Kollega",
        Join = "Anslut",
        Later = "Senare",
        CloseButton = "Stäng",
        StartsIn = "Börjar om {0}:{1:00}   ({2:t})",
        StartedNow = "BÖRJAR NU!",
        StartedAgo = "Började för {0} min sedan",
        ChatHeader = "Teams — {0} olästa",
        MoreChats = "+ {0} chattar till",
        MarkAllSeen = "Markera alla som sedda",
        SeenTooltip = "Sedd (tills nya meddelanden kommer)",
        AlreadyRunning = "Meeting Alarm körs redan (se meddelandefältet).",
        AutostartQuestion = "Starta Meeting Alarm automatiskt med Windows?",
        ConfigCreated = "Inställningsfilen har skapats:\n{0}\n\nFyll i Tenant (t.ex. foretag.se) och LoginHint (din jobbmejl) för varje jobb. " +
                        "Kalendrar från annat håll kan läggas till som ICS-länkar. Spara; ändringar läses in automatiskt.",
        ConfigErrorAtStart = "Fel i {0}:\n\n{1}",
        NoTitle = "(ingen rubrik)",
        UnknownChat = "(chatt)",
        TooManyRequests = "för många förfrågningar, pausar till {0:T}",
    };

    internal static readonly IReadOnlyDictionary<string, Texts> All = new Dictionary<string, Texts>(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = EnUs,
        ["en-GB"] = EnGb,
        ["nl-NL"] = NlNl,
        ["sv-SE"] = SvSe,
    };

    public static Texts T { get; private set; } = EnUs;
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Pick the language: "nl-NL", "sv", "en-GB", … or empty for the Windows display language.
    /// Exact match, otherwise the same language (nl-BE → nl-NL, en-AU → en-GB), otherwise en-US.
    /// </summary>
    public static void UseLanguage(string? language) => (Culture, T) = Resolve(language, CultureInfo.CurrentUICulture);

    internal static (CultureInfo, Texts) Resolve(string? language, CultureInfo system)
    {
        var requested = system;
        if (!string.IsNullOrWhiteSpace(language))
        {
            // predefinedOnly: otherwise any well-formed tag ("not-a-language") yields a made-up culture.
            try { requested = CultureInfo.GetCultureInfo(language.Trim(), predefinedOnly: true); }
            catch (CultureNotFoundException) { }
        }

        var name = All.ContainsKey(requested.Name) ? requested.Name
            : requested.TwoLetterISOLanguageName switch
            {
                "nl" => "nl-NL",
                "sv" => "sv-SE",
                "en" => requested.Name is "en" or "en-US" or "en-CA" or "en-PH" or "en-029" ? "en-US" : "en-GB",
                _ => "en-US",
            };
        return (CultureInfo.GetCultureInfo(name), All[name]);
    }

    /// <summary>Format in the chosen culture (times, numbers).</summary>
    public static string F(string format, params object?[] args) => string.Format(Culture, format, args);
}
