// Meeting Alarm - grote, blijvende popups zodat je geen werk mist:
//  * meetings: popup voordat een meeting begint (agenda's via hun gepubliceerde ICS-link)
//  * Teams-chats: popup met ongelezen chats (Microsoft Graph, per tenant ingelogd)
//
// Draaien:   dotnet run --project src/MeetingAlarm
// Testen:    dotnet run --project src/MeetingAlarm -- --test
// Exe maken: dotnet publish src/MeetingAlarm -o publish
// Instellingen: %APPDATA%\MeetingAlarm\config.json (wordt bij eerste start aangemaakt,
//               wijzigingen worden automatisch geladen)

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "MeetingAlarm_SingleInstance", out bool eerste);
        if (!eerste)
        {
            MessageBox.Show("Meeting Alarm draait al (zie systeemvak).", "Meeting Alarm");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var config = Config.LaadOfMaak();
        if (config is null) return;
        Application.Run(new AlarmContext(config, args.Contains("--test")));
    }
}
