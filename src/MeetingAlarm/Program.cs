// Meeting Alarm - big, persistent popups so you don't miss work:
//  * meetings: a popup before a meeting starts (calendars via their published ICS link)
//  * Teams chats: a popup listing unread chats (Microsoft Graph, signed in per tenant)
//
// Run:       dotnet run --project src/MeetingAlarm
// Test:      dotnet run --project src/MeetingAlarm -- --test
// Build exe: dotnet publish src/MeetingAlarm -o publish
// Settings:  %APPDATA%\MeetingAlarm\config.json (created on first start, changes are picked up automatically)

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        UseLanguage(null);   // Windows display language until the config is loaded
        using var mutex = new Mutex(true, "MeetingAlarm_SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show(T.AlreadyRunning, "Meeting Alarm");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = Config.LoadOrCreate();
        if (config is null) return;
        Application.Run(new AlarmContext(config, args.Contains("--test")));
    }
}
