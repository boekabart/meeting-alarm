using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

// De config-klassen spiegelen config.json en zijn daarom Engels; de rest van de code is Nederlands.

enum Position { TopLeft, Top, TopRight, MiddleLeft, MiddleRight, BottomLeft, Bottom, BottomRight }

sealed class CalendarConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>"#rrggbb", "#rgb" of een CSS-kleurnaam ("teal", "rebeccapurple").</summary>
    public string Color { get; set; } = "#c62828";
}

sealed class TeamsConfig
{
    public string Name { get; set; } = "";
    public string Tenant { get; set; } = "";
    public string LoginHint { get; set; } = "";
    public string Color { get; set; } = "#c62828";
    public string? ClientId { get; set; }
}

sealed class MeetingsConfig
{
    public Position Position { get; set; } = Position.BottomRight;
    public int MinutesBefore { get; set; } = 5;
    public int SnoozeSeconds { get; set; } = 60;
    public int AutoCloseAfterMinutes { get; set; } = 15;
    public int RefreshSeconds { get; set; } = 180;
}

sealed class ChatsConfig
{
    public Position Position { get; set; } = Position.MiddleRight;
    public int PollSeconds { get; set; } = 30;
    public List<string> ChatTypes { get; set; } = ["oneOnOne", "group"];
    public int FlashSeconds { get; set; } = 3;
    public string? ClientId { get; set; }
}

sealed class Config
{
    // FriendlyReminders: multi-tenant app-registratie (HighTech Innovators). Een client-id is geen geheim.
    public const string StandaardClientId = "c3e816c9-59eb-48ae-a7b8-710e8d145bb1";

    public bool Sound { get; set; } = true;
    public MeetingsConfig Meetings { get; set; } = new();
    public ChatsConfig Chats { get; set; } = new();
    public List<CalendarConfig> Calendars { get; set; } = new();
    public List<TeamsConfig> Teams { get; set; } = new();

    [JsonIgnore] public IEnumerable<CalendarConfig> ActieveAgendas => Calendars.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    [JsonIgnore] public IEnumerable<TeamsConfig> ActieveTeams => Teams.Where(t => !string.IsNullOrWhiteSpace(t.Tenant));

    public string ClientIdVoor(TeamsConfig t) =>
        !string.IsNullOrWhiteSpace(t.ClientId) ? t.ClientId
        : !string.IsNullOrWhiteSpace(Chats.ClientId) ? Chats.ClientId
        : StandaardClientId;

    public static string Pad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingAlarm", "config.json");

    static readonly JsonSerializerOptions Opties = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void OpenInKladblok() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Pad}\"") { UseShellExecute = true });

    static void Bewaar(Config c) => File.WriteAllText(Pad, JsonSerializer.Serialize(c, Opties));

    /// <summary>Laadt config.json; een oud (Nederlands, v1) bestand wordt eenmalig omgezet en als config.v1.json bewaard.</summary>
    public static Config Laad()
    {
        var json = File.ReadAllText(Pad);
        if (Migratie.IsV1(json))
        {
            var nieuw = Migratie.VanV1(json);
            File.Copy(Pad, Path.Combine(Path.GetDirectoryName(Pad)!, "config.v1.json"), overwrite: true);
            Bewaar(nieuw);
            return nieuw;
        }
        return JsonSerializer.Deserialize<Config>(json, Opties) ?? new Config();
    }

    public static Config? LaadOfMaak()
    {
        if (!File.Exists(Pad))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            Bewaar(new Config
            {
                Calendars =
                {
                    new CalendarConfig { Name = "Job 1", Url = "PASTE_ICS_LINK_JOB_1", Color = "firebrick" },
                    new CalendarConfig { Name = "Job 2", Url = "PASTE_ICS_LINK_JOB_2", Color = "#1565c0" },
                },
                Teams =
                {
                    new TeamsConfig { Name = "Job 1", Tenant = "", LoginHint = "", Color = "firebrick" },
                },
            });
            if (MessageBox.Show("Meeting Alarm automatisch starten met Windows?", "Meeting Alarm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Autostart.Aan = true;
            MessageBox.Show($"Instellingenbestand aangemaakt:\n{Pad}\n\n" +
                "Vul je ICS-links in, en voor Teams-chats je Tenant (bv. bedrijf.nl) en LoginHint (je werk-e-mail). " +
                "Sla op; wijzigingen worden automatisch geladen.",
                "Meeting Alarm");
            OpenInKladblok();
        }
        try
        {
            return Laad();
        }
        catch (Exception e)
        {
            MessageBox.Show($"Fout in {Pad}:\n\n{e.Message}", "Meeting Alarm");
            return null;
        }
    }

    /// <summary>Omzetten van het v1-formaat (Nederlandse sleutels, één "Agendas"-lijst met ICS én Teams per job).</summary>
    internal static class Migratie
    {
        public static bool IsV1(string json)
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.EnumerateObject().Any(p => p.Name.Equals("Agendas", StringComparison.OrdinalIgnoreCase));
        }

        public static Config VanV1(string json)
        {
            var oud = JsonSerializer.Deserialize<V1>(json, Opties) ?? new V1();
            var d = new MeetingsConfig();
            var c = new ChatsConfig();
            return new Config
            {
                Sound = oud.Geluid ?? true,
                Meetings = new MeetingsConfig
                {
                    Position = Pos(oud.MeetingPositie) ?? d.Position,
                    MinutesBefore = oud.MinutenVooraf ?? d.MinutesBefore,
                    SnoozeSeconds = oud.SnoozeSeconden ?? d.SnoozeSeconds,
                    AutoCloseAfterMinutes = oud.AutoSluitenNaMinuten ?? d.AutoCloseAfterMinutes,
                    RefreshSeconds = oud.VerversSeconden ?? d.RefreshSeconds,
                },
                Chats = new ChatsConfig
                {
                    Position = Pos(oud.ChatPositie) ?? c.Position,
                    PollSeconds = oud.ChatPollSeconden ?? c.PollSeconds,
                    ChatTypes = oud.ChatTypes ?? c.ChatTypes,
                    FlashSeconds = oud.ChatKnipperSeconden ?? c.FlashSeconds,
                    ClientId = oud.ClientId,
                },
                Calendars = oud.Agendas.Where(a => !string.IsNullOrWhiteSpace(a.Url))
                    .Select(a => new CalendarConfig { Name = a.Naam, Url = a.Url!, Color = a.Kleur }).ToList(),
                Teams = oud.Agendas.Where(a => !string.IsNullOrWhiteSpace(a.Tenant))
                    .Select(a => new TeamsConfig { Name = a.Naam, Tenant = a.Tenant!, LoginHint = a.LoginHint ?? "", Color = a.Kleur, ClientId = a.ClientId })
                    .ToList(),
            };
        }

        static Position? Pos(string? s) => s switch
        {
            "LinksBoven" => Position.TopLeft,
            "Boven" => Position.Top,
            "RechtsBoven" => Position.TopRight,
            "LinksMidden" => Position.MiddleLeft,
            "RechtsMidden" => Position.MiddleRight,
            "LinksOnder" => Position.BottomLeft,
            "Onder" => Position.Bottom,
            "RechtsOnder" => Position.BottomRight,
            _ => null,
        };

        sealed class V1
        {
            public int? MinutenVooraf { get; set; }
            public int? SnoozeSeconden { get; set; }
            public int? AutoSluitenNaMinuten { get; set; }
            public int? VerversSeconden { get; set; }
            public bool? Geluid { get; set; }
            public string? MeetingPositie { get; set; }
            public string? ChatPositie { get; set; }
            public int? ChatPollSeconden { get; set; }
            public List<string>? ChatTypes { get; set; }
            public int? ChatKnipperSeconden { get; set; }
            public string? ClientId { get; set; }
            public List<V1Agenda> Agendas { get; set; } = new();
        }

        sealed class V1Agenda
        {
            public string Naam { get; set; } = "";
            public string? Url { get; set; }
            public string Kleur { get; set; } = "#c62828";
            public string? Tenant { get; set; }
            public string? LoginHint { get; set; }
            public string? ClientId { get; set; }
        }
    }
}
