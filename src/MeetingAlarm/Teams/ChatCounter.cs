/// <param name="IsMessage">messageType == "message" and not deleted (so not a system event).</param>
sealed record ChatMessage(DateTimeOffset Created, string? SenderId, bool IsMessage);

/// <param name="Newest">Time of the newest counted message: "seen" acks up to this.</param>
/// <param name="OwnMessage">Time of my own message if that stopped the count: auto-ack up to there.</param>
readonly record struct ChatCount(int Count, DateTimeOffset? Newest, DateTimeOffset? OwnMessage);

static class ChatCounter
{
    /// <summary>Graph returns at most this many messages per page; more is shown as "50+".</summary>
    public const int MaxMessages = 50;

    /// <summary>
    /// A message only counts if it is unread in Teams AND newer than the latest ack.
    /// Either one can only take messages away, never add them: so the later of the two wins.
    /// </summary>
    public static DateTimeOffset? Baseline(DateTimeOffset? ackedAt, DateTimeOffset? readInTeams) =>
        ackedAt is null ? readInTeams
        : readInTeams is null ? ackedAt
        : ackedAt > readInTeams ? ackedAt : readInTeams;

    /// <summary>
    /// Counts messages from others newer than the baseline (<paramref name="newestFirst"/> sorted descending by time).
    /// Stops at the baseline, or at a message of my own: whoever sends something has read the chat.
    /// </summary>
    public static ChatCount Count(IEnumerable<ChatMessage> newestFirst, DateTimeOffset baseline, string myId)
    {
        int count = 0;
        DateTimeOffset? newest = null;
        foreach (var m in newestFirst)
        {
            if (m.Created <= baseline) break;
            if (m.SenderId == myId) return new(count, newest, m.Created);
            if (!m.IsMessage) continue;
            count++;
            newest ??= m.Created;
        }
        return new(count, newest, null);
    }
}
