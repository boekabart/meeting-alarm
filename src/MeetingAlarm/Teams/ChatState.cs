using System.Text.Json;

/// <summary>Per chat, the time of the last acked message. Persisted across restarts.</summary>
sealed class ChatState
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeetingAlarm", "chats.json");

    readonly Dictionary<string, DateTimeOffset> acks;

    public ChatState()
    {
        try
        {
            acks = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            acks = new();   // no (or unreadable) file yet: everything counts from Teams' own read marker
        }
    }

    public static string Key(string tenantId, string chatId) => $"{tenantId}|{chatId}";

    public DateTimeOffset? AckedAt(string key) => acks.TryGetValue(key, out var t) ? t : null;

    /// <summary>Ack up to and including <paramref name="until"/>. Never goes back in time.</summary>
    public void Ack(string key, DateTimeOffset until)
    {
        if (acks.TryGetValue(key, out var old) && old >= until) return;
        acks[key] = until;
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(acks, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
