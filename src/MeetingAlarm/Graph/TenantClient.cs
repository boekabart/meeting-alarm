using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

sealed record ChatRow(TenantConfig Job, string Key, string Name, int Count, DateTimeOffset Newest, string? WebUrl);

sealed class GraphException(string message) : Exception(message);

/// <summary>
/// One tenant via Microsoft Graph, with a delegated sign-in via WAM: its Teams chats and/or its Outlook calendar.
/// </summary>
sealed class TenantClient
{
    static readonly string[] ChatScopes = ["Chat.Read", "User.Read"];
    static readonly string[] CalendarScopes = ["Calendars.Read"];
    static readonly DateTimeOffset StartTime = DateTimeOffset.Now;
    static readonly HttpClient Http = new() { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"), Timeout = TimeSpan.FromSeconds(30) };
    static Task<MsalCacheHelper>? cacheHelper;

    public TenantConfig Tenant { get; }
    /// <summary>Chat polling is paused until an interactive sign-in succeeds.</summary>
    public bool NeedsSignIn { get; set; }
    /// <summary>Last successful chat result; kept when a poll fails.</summary>
    public List<ChatRow> Previous { get; private set; } = [];
    /// <summary>No account known (yet) for this tenant: first sign-in.</summary>
    public bool NeverSignedIn => account is null;

    readonly IPublicClientApplication app;
    readonly IReadOnlyCollection<string> chatTypes;
    readonly ChatState state;
    readonly Dictionary<string, string> names = new();
    IAccount? account;
    bool cacheAttached;
    string? myId, tenantId;
    DateTimeOffset pausedUntil;

    public TenantClient(TenantConfig tenant, string clientId, IReadOnlyCollection<string> chatTypes, ChatState state, Func<IntPtr> ownerWindow)
    {
        Tenant = tenant;
        this.chatTypes = chatTypes;
        this.state = state;
        // Always the specific tenant as authority (not "organizations"): that way each token belongs to the right job.
        app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenant.Tenant)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Meeting Alarm" })
            .WithParentActivityOrWindow(ownerWindow)
            .Build();
    }

    async Task AttachCache()
    {
        if (cacheAttached) return;
        cacheHelper ??= MsalCacheHelper.CreateAsync(new StorageCreationPropertiesBuilder("msal.cache",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingAlarm")).Build());
        (await cacheHelper).RegisterCache(app.UserTokenCache);   // DPAPI-encrypted
        cacheAttached = true;

        var accounts = await app.GetAccountsAsync();
        account = accounts.FirstOrDefault(a => string.Equals(a.Username, Tenant.LoginHint, StringComparison.OrdinalIgnoreCase))
                  ?? (string.IsNullOrWhiteSpace(Tenant.LoginHint) ? accounts.FirstOrDefault() : null);
    }

    async Task<string> Token(string[] scopes, CancellationToken ct)
    {
        await AttachCache();
        AcquireTokenSilentParameterBuilder b;
        if (account is not null) b = app.AcquireTokenSilent(scopes, account);
        else if (!string.IsNullOrWhiteSpace(Tenant.LoginHint)) b = app.AcquireTokenSilent(scopes, Tenant.LoginHint);
        else throw new MsalUiRequiredException("no_account", "Not signed in yet.");
        var res = await b.ExecuteAsync(ct);
        account = res.Account;
        tenantId = res.TenantId;
        return res.AccessToken;
    }

    /// <summary>Interactive sign-in (WAM window) for everything this tenant has switched on. Only call from the UI thread.</summary>
    public async Task SignIn()
    {
        await AttachCache();
        string[] scopes = [.. Tenant.Chats ? ChatScopes : ["User.Read"], .. Tenant.Calendar ? CalendarScopes : []];
        var b = app.AcquireTokenInteractive(scopes);
        if (!string.IsNullOrWhiteSpace(Tenant.LoginHint)) b = b.WithLoginHint(Tenant.LoginHint);
        var res = await b.ExecuteAsync();
        account = res.Account;
        tenantId = res.TenantId;
        NeedsSignIn = false;
    }

    public void Forget(IReadOnlySet<string> keys) => Previous = Previous.Where(r => !keys.Contains(r.Key)).ToList();

    // ------------------------------------------------------------ Calendar

    /// <summary>Meetings from 1 hour ago until 2 days ahead, straight from the mailbox (no publishing delay like ICS).</summary>
    public async Task<List<Meeting>> GetMeetings(CancellationToken ct)
    {
        var from = DateTime.UtcNow.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var until = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        // ponytail: one page of 100 events covers two days; page further if someone ever has more
        var page = await Get($"me/calendarView?startDateTime={from}&endDateTime={until}&$top=100" +
                             "&$select=subject,start,isAllDay,isCancelled,responseStatus,onlineMeeting,iCalUId,id",
            CalendarScopes, ct);
        return page.GetProperty("value").EnumerateArray()
            .Select(e => ToMeeting(e, Tenant.Name, Tenant.Color))
            .OfType<Meeting>()
            .OrderBy(m => m.Start)
            .ToList();
    }

    /// <summary>Graph event → Meeting; null for all-day, cancelled or declined events. Times arrive in UTC (Prefer header).</summary>
    internal static Meeting? ToMeeting(JsonElement e, string calendarName, string color)
    {
        if (e.TryGetProperty("isAllDay", out var allDay) && allDay.ValueKind == JsonValueKind.True) return null;
        if (e.TryGetProperty("isCancelled", out var cancelled) && cancelled.ValueKind == JsonValueKind.True) return null;
        if (e.TryGetProperty("responseStatus", out var rs) && rs.ValueKind == JsonValueKind.Object && rs.Str("response") == "declined") return null;
        if (!e.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Object || start.Date("dateTime") is not { } when) return null;

        var title = e.Str("subject") is { Length: > 0 } s ? s.Trim() : T.NoTitle;
        var link = e.TryGetProperty("onlineMeeting", out var om) && om.ValueKind == JsonValueKind.Object ? om.Str("joinUrl") : null;
        return new Meeting(calendarName, color, title, when.LocalDateTime, e.Str("iCalUId") ?? e.Str("id") ?? title, link);
    }

    // ------------------------------------------------------------ Chats

    public async Task<List<ChatRow>> Poll(CancellationToken ct)
    {
        if (DateTimeOffset.Now < pausedUntil) return Previous;   // Graph asked us to back off (429)

        myId ??= (await Get("me?$select=id", ChatScopes, ct)).Str("id");
        // ponytail: only the 50 most recently active chats (one page); page further if that ever turns out too few
        var chats = await Get("me/chats?$expand=lastMessagePreview&$orderby=lastMessagePreview/createdDateTime desc&$top=50", ChatScopes, ct);

        var rows = new List<ChatRow>();
        foreach (var c in chats.GetProperty("value").EnumerateArray())
        {
            var chatId = c.Str("id");
            if (chatId is null || !chatTypes.Contains(c.Str("chatType") ?? "")) continue;
            if (!c.TryGetProperty("lastMessagePreview", out var preview) || preview.ValueKind != JsonValueKind.Object) continue;
            if (preview.Date("createdDateTime") is not { } latest) continue;

            var key = ChatState.Key(tenantId!, chatId);
            var acked = state.AckedAt(key);
            if (latest <= acked) continue;   // fast path: nothing new since the ack

            // Teams' own read marker. Normally part of the list; if not, fetch it per chat.
            if (!c.TryGetProperty("viewpoint", out var vp))
                (await Get($"chats/{Uri.EscapeDataString(chatId)}", ChatScopes, ct)).TryGetProperty("viewpoint", out vp);
            var read = vp.ValueKind == JsonValueKind.Object ? vp.Date("lastMessageReadDateTime") : null;

            var baseline = ChatCounter.Baseline(acked, read);
            if (baseline is null)
            {
                baseline = StartTime;   // never acked and Teams doesn't know either: count from app start
                state.Ack(key, StartTime);
            }
            if (latest <= baseline) continue;
            if (SenderId(preview) == myId)
            {
                state.Ack(key, latest);   // last message is mine: read
                continue;
            }

            var messages = await Get($"chats/{Uri.EscapeDataString(chatId)}/messages?$top={ChatCounter.MaxMessages}&$orderby=createdDateTime desc", ChatScopes, ct);
            var count = ChatCounter.Count(
                messages.GetProperty("value").EnumerateArray().Select(ToMessage).OrderByDescending(m => m.Created),
                baseline.Value, myId!);
            if (count.OwnMessage is { } own) state.Ack(key, own);
            if (count.Count == 0) continue;

            rows.Add(new ChatRow(Tenant, key, await ResolveName(c, chatId, ct), count.Count, count.Newest!.Value,
                c.Str("webUrl") ?? TeamsLink.ForChat(tenantId!, chatId)));
        }
        Previous = rows;
        return rows;
    }

    async Task<string> ResolveName(JsonElement chat, string chatId, CancellationToken ct)
    {
        var topic = chat.Str("topic");
        if (!string.IsNullOrWhiteSpace(topic)) return topic;
        if (names.TryGetValue(chatId, out var name)) return name;

        var members = await Get($"chats/{Uri.EscapeDataString(chatId)}/members", ChatScopes, ct);
        name = string.Join(", ", members.GetProperty("value").EnumerateArray()
            .Where(m => m.Str("userId") != myId)
            .Select(m => m.Str("displayName"))
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return names[chatId] = name.Length > 0 ? name : T.UnknownChat;
    }

    static ChatMessage ToMessage(JsonElement m)
    {
        bool deleted = m.TryGetProperty("deletedDateTime", out var d) && d.ValueKind != JsonValueKind.Null;
        return new ChatMessage(m.Date("createdDateTime") ?? DateTimeOffset.MinValue, SenderId(m), m.Str("messageType") == "message" && !deleted);
    }

    static string? SenderId(JsonElement m) =>
        m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object &&
        f.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u.Str("id") : null;

    // ------------------------------------------------------------ Graph

    async Task<JsonElement> Get(string path, string[] scopes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token(scopes, ct));
        req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");   // calendar times in UTC; ignored by the chat endpoints
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if ((int)resp.StatusCode == 429)
        {
            pausedUntil = DateTimeOffset.Now + (resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new GraphException(F(T.TooManyRequests, pausedUntil));
        }
        if (!resp.IsSuccessStatusCode)
        {
            var message = body;
            try { message = JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("message").GetString() ?? body; }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
            throw new GraphException($"{(int)resp.StatusCode}: {message}");
        }
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

static class JsonExt
{
    public static string? Str(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static DateTimeOffset? Date(this JsonElement e, string name) =>
        DateTimeOffset.TryParse(e.Str(name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
