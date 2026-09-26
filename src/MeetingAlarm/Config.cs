using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

enum Position { TopLeft, Top, TopRight, MiddleLeft, MiddleRight, BottomLeft, Bottom, BottomRight }

sealed class CalendarConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>"#rrggbb", "#rgb" or a CSS color name ("teal", "rebeccapurple").</summary>
    public string Color { get; set; } = "#c62828";
}

/// <summary>One Microsoft 365 tenant (job), signed in via Graph. Chats and calendar are separate opt-ins.</summary>
sealed class TenantConfig
{
    public string Name { get; set; } = "";
    public string Tenant { get; set; } = "";
    public string LoginHint { get; set; } = "";
    public string Color { get; set; } = "#c62828";
    public string? ClientId { get; set; }
    /// <summary>Show unread Teams chats of this tenant.</summary>
    public bool Chats { get; set; }
    /// <summary>Meeting popups from this tenant's Outlook calendar (Graph; much faster than a published ICS link).</summary>
    public bool Calendar { get; set; }
}

sealed class MeetingsConfig
{
    public Position Position { get; set; } = Position.BottomRight;
    /// <summary>0 = main screen; n = Windows display number n (Settings → Display → Identify), falling back to the main screen.</summary>
    public int Screen { get; set; }
    public int MinutesBefore { get; set; } = 5;
    public int SnoozeSeconds { get; set; } = 60;
    /// <summary>Minutes after the start at which the popup closes itself; 0 = never.</summary>
    public int AutoCloseAfterMinutes { get; set; } = 15;
    /// <summary>How often published ICS calendars are fetched.</summary>
    public int RefreshSeconds { get; set; } = 180;
    /// <summary>How often tenant calendars are read via Graph.</summary>
    public int GraphRefreshSeconds { get; set; } = 30;
}

sealed class ChatsConfig
{
    public Position Position { get; set; } = Position.MiddleRight;
    /// <summary>See <see cref="MeetingsConfig.Screen"/>.</summary>
    public int Screen { get; set; }
    public int PollSeconds { get; set; } = 30;
    public List<string> ChatTypes { get; set; } = ["oneOnOne", "group"];
    public int FlashSeconds { get; set; } = 3;
    public string? ClientId { get; set; }
}

sealed class Config
{
    // FriendlyReminders: multi-tenant app registration (HighTech Innovators). A client ID is not a secret.
    public const string DefaultClientId = "c3e816c9-59eb-48ae-a7b8-710e8d145bb1";

    /// <summary>"en-US", "en-GB", "nl-NL", "sv-SE" (or short: "nl", "sv"); empty = Windows display language.</summary>
    public string? Language { get; set; }
    public bool Sound { get; set; } = true;
    public MeetingsConfig Meetings { get; set; } = new();
    public ChatsConfig Chats { get; set; } = new();
    public List<TenantConfig> Tenants { get; set; } = new();
    /// <summary>Published ICS calendars, for calendars outside the tenants above.</summary>
    public List<CalendarConfig> Calendars { get; set; } = new();

    [JsonIgnore] public IEnumerable<CalendarConfig> ActiveCalendars => Calendars.Where(c => !string.IsNullOrWhiteSpace(c.Url));
    [JsonIgnore] public IEnumerable<TenantConfig> ActiveTenants =>
        Tenants.Where(t => !string.IsNullOrWhiteSpace(t.Tenant) && (t.Chats || t.Calendar));

    public string ClientIdFor(TenantConfig t) =>
        !string.IsNullOrWhiteSpace(t.ClientId) ? t.ClientId
        : !string.IsNullOrWhiteSpace(Chats.ClientId) ? Chats.ClientId
        : DefaultClientId;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingAlarm", "config.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void OpenInNotepad() =>
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{FilePath}\"") { UseShellExecute = true });

    static void Save(Config c) => File.WriteAllText(FilePath, JsonSerializer.Serialize(c, Options));

    public static Config Load() =>
        JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath), Options) ?? new Config();

    public static Config? LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Save(new Config
            {
                Language = "",
                Tenants =
                {
                    new TenantConfig { Name = "Job 1", Tenant = "", LoginHint = "", Color = "firebrick", Chats = true, Calendar = true },
                    new TenantConfig { Name = "Job 2", Tenant = "", LoginHint = "", Color = "#1565c0", Chats = true, Calendar = true },
                },
            });
            if (MessageBox.Show(T.AutostartQuestion, "Meeting Alarm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Autostart.Enabled = true;
            MessageBox.Show(F(T.ConfigCreated, FilePath), "Meeting Alarm");
            OpenInNotepad();
        }
        try
        {
            var cfg = Load();
            UseLanguage(cfg.Language);
            return cfg;
        }
        catch (Exception e)
        {
            MessageBox.Show(F(T.ConfigErrorAtStart, FilePath, e.Message), "Meeting Alarm");
            return null;
        }
    }
}
