using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

sealed record ChatRij(TeamsConfig Job, string Sleutel, string Naam, int Aantal, DateTimeOffset Nieuwste, string? WebUrl);

sealed class GraphFout(string bericht) : Exception(bericht);

/// <summary>Leest de chats van één job (tenant) via Microsoft Graph, met een gedelegeerde login via WAM.</summary>
sealed class ChatPoller
{
    static readonly string[] Scopes = ["Chat.Read", "User.Read"];
    static readonly DateTimeOffset StartTijd = DateTimeOffset.Now;
    static readonly HttpClient Http = new() { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"), Timeout = TimeSpan.FromSeconds(30) };
    static Task<MsalCacheHelper>? cacheHelper;

    public TeamsConfig Job { get; }
    public bool InloggenNodig { get; set; }
    /// <summary>Laatste geslaagde resultaat; blijft staan als een poll mislukt.</summary>
    public List<ChatRij> Vorige { get; private set; } = [];
    /// <summary>Er is (nog) geen account voor deze job bekend: eerste keer inloggen.</summary>
    public bool NooitIngelogd => account is null;

    readonly IPublicClientApplication app;
    readonly IReadOnlyCollection<string> chatTypes;
    readonly ChatState state;
    readonly Dictionary<string, string> namen = new();
    IAccount? account;
    bool cacheGekoppeld;
    string? mijnId, tenantId;
    DateTimeOffset pauzeTot;

    public ChatPoller(TeamsConfig job, string clientId, IReadOnlyCollection<string> chatTypes, ChatState state, Func<IntPtr> eigenaarVenster)
    {
        Job = job;
        this.chatTypes = chatTypes;
        this.state = state;
        // Altijd de specifieke tenant als authority (niet "organizations"): zo hoort elk token bij de juiste job.
        app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, job.Tenant)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Meeting Alarm" })
            .WithParentActivityOrWindow(eigenaarVenster)
            .Build();
    }

    async Task KoppelCache()
    {
        if (cacheGekoppeld) return;
        cacheHelper ??= MsalCacheHelper.CreateAsync(new StorageCreationPropertiesBuilder("msal.cache",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetingAlarm")).Build());
        (await cacheHelper).RegisterCache(app.UserTokenCache);   // DPAPI-versleuteld
        cacheGekoppeld = true;

        var accounts = await app.GetAccountsAsync();
        account = accounts.FirstOrDefault(a => string.Equals(a.Username, Job.LoginHint, StringComparison.OrdinalIgnoreCase))
                  ?? (string.IsNullOrWhiteSpace(Job.LoginHint) ? accounts.FirstOrDefault() : null);
    }

    async Task<string> Token(CancellationToken ct)
    {
        await KoppelCache();
        AcquireTokenSilentParameterBuilder b;
        if (account is not null) b = app.AcquireTokenSilent(Scopes, account);
        else if (!string.IsNullOrWhiteSpace(Job.LoginHint)) b = app.AcquireTokenSilent(Scopes, Job.LoginHint);
        else throw new MsalUiRequiredException("geen_account", "Nog niet ingelogd.");
        var res = await b.ExecuteAsync(ct);
        account = res.Account;
        tenantId = res.TenantId;
        return res.AccessToken;
    }

    /// <summary>Interactief inloggen (WAM-venster). Alleen vanaf de UI-thread aanroepen.</summary>
    public async Task Inloggen()
    {
        await KoppelCache();
        var b = app.AcquireTokenInteractive(Scopes);
        if (!string.IsNullOrWhiteSpace(Job.LoginHint)) b = b.WithLoginHint(Job.LoginHint);
        var res = await b.ExecuteAsync();
        account = res.Account;
        tenantId = res.TenantId;
        InloggenNodig = false;
    }

    public void Vergeet(IReadOnlySet<string> sleutels) => Vorige = Vorige.Where(r => !sleutels.Contains(r.Sleutel)).ToList();

    public async Task<List<ChatRij>> Poll(CancellationToken ct)
    {
        if (DateTimeOffset.Now < pauzeTot) return Vorige;   // Graph vroeg om even te wachten (429)

        mijnId ??= (await Get("me?$select=id", ct)).Str("id");
        // ponytail: alleen de 50 meest recent actieve chats (één pagina); pagineren als dat ooit te weinig blijkt
        var chats = await Get("me/chats?$expand=lastMessagePreview&$orderby=lastMessagePreview/createdDateTime desc&$top=50", ct);

        var rijen = new List<ChatRij>();
        foreach (var c in chats.GetProperty("value").EnumerateArray())
        {
            var chatId = c.Str("id");
            if (chatId is null || !chatTypes.Contains(c.Str("chatType") ?? "")) continue;
            if (!c.TryGetProperty("lastMessagePreview", out var preview) || preview.ValueKind != JsonValueKind.Object) continue;
            if (preview.Datum("createdDateTime") is not { } laatste) continue;

            var sleutel = ChatState.Sleutel(tenantId!, chatId);
            var acked = state.AckedAt(sleutel);
            if (laatste <= acked) continue;   // snel pad: niets nieuws sinds de ack

            // Teams' eigen gelezen-markering. Zit normaal in de lijst; zo niet, dan per chat ophalen.
            if (!c.TryGetProperty("viewpoint", out var vp))
                (await Get($"chats/{Uri.EscapeDataString(chatId)}", ct)).TryGetProperty("viewpoint", out vp);
            var gelezen = vp.ValueKind == JsonValueKind.Object ? vp.Datum("lastMessageReadDateTime") : null;

            var basis = ChatTeller.Basislijn(acked, gelezen);
            if (basis is null)
            {
                basis = StartTijd;   // nooit geackt en Teams weet het ook niet: tellen vanaf de start van de app
                state.Ack(sleutel, StartTijd);
            }
            if (laatste <= basis) continue;
            if (Van(preview) == mijnId)
            {
                state.Ack(sleutel, laatste);   // laatste bericht is van mij: gelezen
                continue;
            }

            var berichten = await Get($"chats/{Uri.EscapeDataString(chatId)}/messages?$top={ChatTeller.MaxBerichten}&$orderby=createdDateTime desc", ct);
            var telling = ChatTeller.Tel(
                berichten.GetProperty("value").EnumerateArray().Select(NaarBericht).OrderByDescending(b => b.Gemaakt),
                basis.Value, mijnId!);
            if (telling.EigenBericht is { } eigen) state.Ack(sleutel, eigen);
            if (telling.Aantal == 0) continue;

            rijen.Add(new ChatRij(Job, sleutel, await Naam(c, chatId, ct), telling.Aantal, telling.Nieuwste!.Value, c.Str("webUrl")));
        }
        Vorige = rijen;
        return rijen;
    }

    async Task<string> Naam(JsonElement chat, string chatId, CancellationToken ct)
    {
        var topic = chat.Str("topic");
        if (!string.IsNullOrWhiteSpace(topic)) return topic;
        if (namen.TryGetValue(chatId, out var naam)) return naam;

        var leden = await Get($"chats/{Uri.EscapeDataString(chatId)}/members", ct);
        naam = string.Join(", ", leden.GetProperty("value").EnumerateArray()
            .Where(m => m.Str("userId") != mijnId)
            .Select(m => m.Str("displayName"))
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return namen[chatId] = naam.Length > 0 ? naam : T.OnbekendeChat;
    }

    static Bericht NaarBericht(JsonElement m)
    {
        bool verwijderd = m.TryGetProperty("deletedDateTime", out var d) && d.ValueKind != JsonValueKind.Null;
        return new Bericht(m.Datum("createdDateTime") ?? DateTimeOffset.MinValue, Van(m), m.Str("messageType") == "message" && !verwijderd);
    }

    static string? Van(JsonElement m) =>
        m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object &&
        f.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? u.Str("id") : null;

    async Task<JsonElement> Get(string pad, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, pad);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token(ct));
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if ((int)resp.StatusCode == 429)
        {
            pauzeTot = DateTimeOffset.Now + (resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new GraphFout(F(T.TeVeelVerzoeken, pauzeTot));
        }
        if (!resp.IsSuccessStatusCode)
        {
            var melding = body;
            try { melding = JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("message").GetString() ?? body; }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
            throw new GraphFout($"{(int)resp.StatusCode}: {melding}");
        }
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}

static class JsonExt
{
    public static string? Str(this JsonElement e, string naam) =>
        e.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static DateTimeOffset? Datum(this JsonElement e, string naam) =>
        DateTimeOffset.TryParse(e.Str(naam), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
