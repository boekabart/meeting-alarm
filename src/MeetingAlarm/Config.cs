using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

enum Positie { LinksBoven, Boven, RechtsBoven, LinksMidden, RechtsMidden, LinksOnder, Onder, RechtsOnder }

/// <summary>Eén job: agenda (ICS) en/of Teams-tenant, met een gedeelde naam en kleur.</summary>
sealed class AgendaConfig
{
    public string Naam { get; set; } = "";
    public string Url { get; set; } = "";
    public string Kleur { get; set; } = "#c62828";

    // Teams-chats (optioneel): zonder Tenant geen chatbewaking voor deze job.
    public string? Tenant { get; set; }
    public string? LoginHint { get; set; }
    public string? ClientId { get; set; }

    [JsonIgnore] public bool HeeftAgenda => !string.IsNullOrWhiteSpace(Url);
    [JsonIgnore] public bool HeeftTeams => !string.IsNullOrWhiteSpace(Tenant);
}

sealed class Config
{
    // FriendlyReminders: multi-tenant app-registratie (HighTech Innovators). Een client-id is geen geheim.
    public const string StandaardClientId = "c3e816c9-59eb-48ae-a7b8-710e8d145bb1";

    public int MinutenVooraf { get; set; } = 5;
    public int SnoozeSeconden { get; set; } = 60;
    public int AutoSluitenNaMinuten { get; set; } = 15;
    public int VerversSeconden { get; set; } = 180;
    public bool Geluid { get; set; } = true;

    public Positie MeetingPositie { get; set; } = Positie.RechtsOnder;
    public Positie ChatPositie { get; set; } = Positie.RechtsMidden;
    public int ChatPollSeconden { get; set; } = 30;
    public List<string> ChatTypes { get; set; } = ["oneOnOne", "group"];
    public int ChatKnipperSeconden { get; set; } = 3;
    public string? ClientId { get; set; }

    public List<AgendaConfig> Agendas { get; set; } = new();

    public string ClientIdVoor(AgendaConfig a) =>
        !string.IsNullOrWhiteSpace(a.ClientId) ? a.ClientId
        : !string.IsNullOrWhiteSpace(ClientId) ? ClientId
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

    public static Config Laad() =>
        JsonSerializer.Deserialize<Config>(File.ReadAllText(Pad), Opties) ?? new Config();

    public static Config? LaadOfMaak()
    {
        if (!File.Exists(Pad))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Pad)!);
            var voorbeeld = new Config
            {
                Agendas =
                {
                    new AgendaConfig { Naam = "Job 1", Url = "PLAK_HIER_ICS_LINK_JOB_1", Kleur = "#c62828", Tenant = "", LoginHint = "" },
                    new AgendaConfig { Naam = "Job 2", Url = "PLAK_HIER_ICS_LINK_JOB_2", Kleur = "#1565c0", Tenant = "", LoginHint = "" },
                }
            };
            File.WriteAllText(Pad, JsonSerializer.Serialize(voorbeeld, Opties));
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
}
