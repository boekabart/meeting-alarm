using System.Text.Json;

/// <summary>Per chat het tijdstip van het laatst geackte bericht. Blijft bewaard over herstarts heen.</summary>
sealed class ChatState
{
    static string Pad => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingAlarm", "chats.json");

    readonly Dictionary<string, DateTimeOffset> acks;

    public ChatState()
    {
        try
        {
            acks = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(Pad)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            acks = new();   // nog geen (of onleesbaar) bestand: alles telt vanaf Teams' eigen gelezen-markering
        }
    }

    public static string Sleutel(string tenantId, string chatId) => $"{tenantId}|{chatId}";

    public DateTimeOffset? AckedAt(string sleutel) => acks.TryGetValue(sleutel, out var t) ? t : null;

    /// <summary>Ack tot en met <paramref name="tot"/>. Gaat nooit terug in de tijd.</summary>
    public void Ack(string sleutel, DateTimeOffset tot)
    {
        if (acks.TryGetValue(sleutel, out var oud) && oud >= tot) return;
        acks[sleutel] = tot;
        var tmp = Pad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(acks, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, Pad, overwrite: true);
    }
}
