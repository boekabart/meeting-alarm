using System.ComponentModel;
using System.Diagnostics;

static class TeamsLink
{
    /// <summary>Same shape as Graph's chat.webUrl; used when Graph doesn't send one.</summary>
    public static string ForChat(string tenantId, string chatId) =>
        $"https://teams.microsoft.com/l/chat/{Uri.EscapeDataString(chatId)}/0?tenantId={Uri.EscapeDataString(tenantId)}";

    /// <summary>https Teams link → msteams: link, so the Teams app opens it instead of the browser first.</summary>
    public static string? ToApp(string webUrl) =>
        Uri.TryCreate(webUrl, UriKind.Absolute, out var u) &&
        (u.Host.Equals("teams.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("teams.cloud.microsoft", StringComparison.OrdinalIgnoreCase))
            ? "msteams:" + u.PathAndQuery
            : null;

    /// <summary>Open in the Teams app; if that fails (no app / protocol not registered), in the browser.</summary>
    public static void Open(string webUrl)
    {
        if (ToApp(webUrl) is { } app)
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
