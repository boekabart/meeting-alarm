using System.ComponentModel;
using System.Diagnostics;

static class TeamsLink
{
    /// <summary>Zelfde vorm als Graph's chat.webUrl; gebruikt als Graph die niet meestuurt.</summary>
    public static string VoorChat(string tenantId, string chatId) =>
        $"https://teams.microsoft.com/l/chat/{Uri.EscapeDataString(chatId)}/0?tenantId={Uri.EscapeDataString(tenantId)}";

    /// <summary>https-teamslink → msteams:-link, zodat de Teams-app hem opent in plaats van eerst de browser.</summary>
    public static string? NaarApp(string webUrl) =>
        Uri.TryCreate(webUrl, UriKind.Absolute, out var u) &&
        (u.Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("teams.cloud.microsoft", StringComparison.OrdinalIgnoreCase))
            ? "msteams:" + u.PathAndQuery
            : null;

    /// <summary>Open in de Teams-app; lukt dat niet (geen app / protocol niet geregistreerd), dan in de browser.</summary>
    public static void Open(string webUrl)
    {
        if (NaarApp(webUrl) is { } app)
        {
            try
            {
                Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
                return;
            }
            catch (Win32Exception) { }
        }
        Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
    }
}
