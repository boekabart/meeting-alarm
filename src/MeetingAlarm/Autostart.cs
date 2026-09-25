using Microsoft.Win32;

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Naam = "MeetingAlarm";

    public static bool Aan
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Naam) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(Naam, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Naam, false);
        }
    }
}
