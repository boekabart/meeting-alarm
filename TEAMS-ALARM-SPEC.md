# Meeting Alarm — Teams chats extension (spec)

Extend Meeting Alarm with a **second, separate popup** that lists Teams chats with messages I haven't
acknowledged yet, across multiple tenants (2 jobs). One app, one tray icon, one config. The goal is the
same as the meeting popup: don't miss work.

## 1. Goal

- See at a glance, without opening Teams, **which chats** have new messages and **how many**.
- The chat popup exists **only while at least one chat has unacknowledged messages**.
- Acknowledging a chat ("ack") hides it **until a newer message arrives in that chat**. Acks survive restarts.
- Both employer tenants at the same time.

### Non-goals (v1)

- Reading, replying to, or previewing message content. Only the chat name and a count.
- Muting a whole chat permanently (e.g. a noisy group chat). A possible later feature. Ack ≠ mute.
- Channel messages. Only chats.
- Push/webhooks. Graph change notifications need a public HTTPS endpoint, so v1 polls.

## 2. Decisions

| # | Decision |
|---|---|
| D1 | A chat clears by **either** an ack in the app **or** reading it in Teams, whichever is later (§4). |
| D2 | Acks are persistent across restarts. There is no "reset on startup". |
| D3 | Merged into `MeetingAlarm.cs`, but as a **separate popup**. Default positions: meetings bottom-right, chats middle-right. **Both positions are configurable.** |

## 3. Access: Microsoft Graph, delegated, per tenant

Teams has nothing like the calendar's ICS link, so each tenant needs its own delegated login.

| Item | Value |
|---|---|
| Scopes | `Chat.Read`, `User.Read`, `offline_access` |
| Library | `Microsoft.Identity.Client` + `Microsoft.Identity.Client.Broker` (WAM → SSO with the work accounts already signed in to Windows, satisfies most Conditional Access device checks) |
| Token cache | `Microsoft.Identity.Client.Extensions.Msal`, DPAPI-encrypted, `%LOCALAPPDATA%\MeetingAlarm\msal.cache` |
| Client ID | Built-in default `c3e816c9-59eb-48ae-a7b8-710e8d145bb1` (FriendlyReminders). It can be overridden globally or per job (§6). A client ID is not a secret. |

### App registration — DONE 2026-09-25

**FriendlyReminders**, app ID `c3e816c9-59eb-48ae-a7b8-710e8d145bb1`, registered in the HighTech Innovators tenant and admin-consented there.
Homepage `https://hightechinnovators.nl`. The publisher domain still needs to be set in the portal (Branding & properties), because Graph treats it as read-only.
It was created with these settings:

- **Name**: `FriendlyReminders`
- **Supported account types**: *Accounts in any organizational directory (multi-tenant)*
- **Authentication** → add platform *Mobile and desktop applications*:
  - `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` (WAM)
  - `http://localhost` (system-browser fallback)
  - *Allow public client flows*: Yes
- **API permissions** → Microsoft Graph → Delegated: `Chat.Read`, `User.Read`, `offline_access`
- **No client secret or certificate.** A public client can't keep one safe.

The registration lives in my tenant. Every other tenant gets a *service principal* the moment consent is given there:

| Tenant | How consent happens |
|---|---|
| My own (I'm admin) | App registration → API permissions → *Grant admin consent for <tenant>*. |
| The other employer | User consent is blocked there (it showed "Approval required" even for Graph Explorer). Two routes: **(a)** sign in, get the prompt, and send the justification through the tenant's admin consent workflow; or **(b)** send their admin the link `https://login.microsoftonline.com/{tenant}/adminconsent?client_id={clientId}`, which grants it for the whole tenant in one click. |

Justification text for (a):
> Personal desktop reminder (Meeting Alarm) that shows which Teams chats have unread messages. Delegated `Chat.Read` for my own
> account only. It stores no message content (only timestamps and chat names, locally) and has no client secret.

The admin will want to see a **publisher**. Setting *Branding → Publisher domain* to my company's verified domain in the
registration helps. A full "verified publisher" (MPN ID) is not required for admin consent.

**Per-job override:** `ClientId` can also be set on a job, e.g. if the other employer prefers a single-tenant registration
in their own tenant. Nothing else changes.

If a tenant refuses consent altogether, see risk R1.

## 4. Counting and ack semantics

State is kept per chat, keyed by `tenantId|chatId`, with one value: `AckedAt`, the timestamp of the last acknowledged message.

```
baseline = max(AckedAt, chat.viewpoint.lastMessageReadDateTime)   -- evaluated on every poll
```

**Unread count** = messages with `createdDateTime > baseline` and `messageType == "message"` that are
not from me and not deleted.

Put differently, a message counts **only if both** of these are true:
1. it is **unread in Teams** (newer than `lastMessageReadDateTime`), **and**
2. it is **newer than the latest ack** for that chat (`AckedAt`).

Neither side can ever add messages. Each can only take them away.

- **Read in Teams** → `lastMessageReadDateTime` moves forward, so the row disappears on the next poll. Nothing is stored,
  because Teams remembers it.
- **First sight** of a chat (no state yet) needs no special case: the count is simply what Teams considers unread.
  If Teams has no read marker either, persist `AckedAt = <first-seen time>`.
- **Ack** (`✓`): `AckedAt = createdDateTime of the newest message the popup showed`, **not** the wall clock at click time.
  A message that arrives between the last poll and the click therefore still counts.
- **Alles gezien**: acks every visible row, each with its own shown timestamp.
- **My own message** in a chat: I'm clearly in it, so it auto-acks up to that message.
- A row reappears as soon as its count is > 0 again.
- Edited messages don't count again, because counting uses `createdDateTime`.

Persisted in `%APPDATA%\MeetingAlarm\chats.json`, written after every change (write to a temp file, then rename):

```json
{ "tenantGuid|19:abc...@thread.v2": "2026-09-25T14:03:11.123Z" }
```

## 5. Polling (per tenant, every `Chats.PollSeconds`, default 30)

1. `GET /me` once → my user id (`meId`).
2. `GET /me/chats?$expand=lastMessagePreview&$orderby=lastMessagePreview/createdDateTime desc&$top=50`
   - Keep only chats whose `chatType` is in `ChatTypes` (default `oneOnOne`, `group`; `meeting` is excluded).
   - Stop paging once `lastMessagePreview.createdDateTime` is older than the oldest baseline in play.
   - `viewpoint` must come along in this call, because it's needed on every poll (R5).
3. For each chat where `lastMessagePreview.createdDateTime > baseline`:
   - Last message from me → auto-ack, skip.
   - Otherwise `GET /chats/{id}/messages?$top=50&$orderby=createdDateTime desc` and count per §4, stopping at the first
     message ≤ baseline. At 50 or more, show `50+`.
4. Name: `topic` if set. Otherwise the other member's `displayName` from `GET /chats/{id}/members`, cached for the app's lifetime.
5. Publish `(job, chatId, naam, aantal, newestCreated)` to the UI thread.

Cost: 1 request per tenant per poll, plus 1 or 2 per chat that actually changed.

## 6. Config (`config.json`, v2)

Calendars and Teams tenants are **separate lists**: one tenant can have several ICS calendars, and vice versa.
Keys are English. (The Dutch v1 format and its auto-migration have been removed.)

```json
{
  "Language": "",
  "Sound": true,
  "Meetings": { "Position": "BottomRight", "MinutesBefore": 5, "SnoozeSeconds": 60, "AutoCloseAfterMinutes": 15, "RefreshSeconds": 180 },
  "Chats":    { "Position": "MiddleRight", "PollSeconds": 30, "ChatTypes": ["oneOnOne", "group"], "FlashSeconds": 3 },
  "Calendars": [
    { "Name": "Job 1", "Url": "https://…/calendar.ics", "Color": "firebrick" },
    { "Name": "Job 1 (team)", "Url": "https://…/team.ics", "Color": "#c62828" }
  ],
  "Teams": [
    { "Name": "Job 1", "Tenant": "job1.nl", "LoginHint": "bart@job1.nl", "Color": "firebrick" }
  ]
}
```

- `Tenant` is a domain or tenant GUID, used in the authority `https://login.microsoftonline.com/{Tenant}`. It is always the
  **specific tenant**, never `organizations`/`common`, so each token belongs to the right tenant.
- `ClientId` is optional in `Chats` and per Teams entry. Resolution order: entry → `Chats` → built-in constant (FriendlyReminders).
- `Color`: `#rrggbb`, `#rgb`, or a CSS color name (`teal`, `rebeccapurple`, `slategrey`). Anything invalid falls back to firebrick.
- **Positions**: `TopLeft`, `Top`, `TopRight`, `MiddleLeft`, `MiddleRight`, `BottomLeft`, `Bottom`, `BottomRight`,
  relative to the primary screen's working area, with a 10 px margin.
- `Language`: `en-US`, `en-GB`, `nl-NL`, `sv-SE` (or short: `nl`, `sv`, `en`). Blank means the Windows display language.
  Other variants of a language fall back to the closest one (nl-BE → nl-NL, en-AU → en-GB), and anything else falls back to en-US.
  Times use the chosen culture's short time format (en-US "2:03 PM", the others "14:03").
- `Screen` (in `Meetings` and `Chats`): `0` (the default) = main screen. `n` = Windows display number n
  (Settings → Display → Identify), falling back to the main screen if that display isn't there. Popups move along when screens are plugged in or out.
- `Meetings.AutoCloseAfterMinutes`: `0` = never close by itself.
- Everything reloads live (FileSystemWatcher), including positions and language.

## 7. UI

### Shared popup base

Extract a base class from the current `PopupForm`: borderless, topmost, `ShowWithoutActivation`, `WS_EX_TOOLWINDOW`,
DPI scaling, re-asserting `HWND_TOPMOST`, and positioning at an anchor. The meeting popup and the chat popup derive from it.

### Meeting popups (existing, one change)

- They are placed at `MeetingPositie` instead of a hard-coded bottom-right.
- Stacking grows away from the anchor edge: `…Onder` stacks upward, `…Boven` downward, `…Midden` stacks centered around the middle.

### Chat popup (new, a single window)

- Placed at `ChatPositie`. For `…Midden` anchors it is vertically centered and grows equally up and down as rows are added.
- Header: "Teams — N ongelezen" (N = the sum of all counts).
- One row per chat, newest message first:
  - A color bar with the job's `Kleur`, the job name in small text, the **chat name**, and the **count** in large text.
  - Click the name → open it in Teams: `https://teams.microsoft.com/l/chat/{chatId}/0?tenantId={tenantId}`. This does **not** ack.
  - A `✓` button with the tooltip "Gezien (tot er nieuwe berichten komen)" → ack.
- Footer: `Alles gezien`.
- Max 10 rows, then "+ N andere chats".
- The popup shows when the total goes from 0 to more than 0, and hides when it goes back to 0. There is no close button:
  ack is the only way to make it go away, which is the whole point.
- **Flash** when any count goes up or a row appears: `ChatKnipperSeconden` of blinking in the existing blink color,
  plus `SystemSounds` if `Geluid` is on. A decrease (from an ack) doesn't flash.

### Tray menu (additions)

- One status line per job that has a tenant: `Job 1 Teams: OK om 14:03` / `FOUT – …` / `inloggen vereist`.
- `Inloggen bij Job 1…`, shown only while that job needs it.
- `Chat-popup tonen`: shows it even when empty (as a test and to check the position).

## 8. Auth flow

- Startup: `AcquireTokenSilent(LoginHint)` per job. If it fails with `MsalUiRequiredException`, show "inloggen vereist",
  **one** balloon, and the menu item. Never pop up an interactive login by itself, except the first time a `Tenant` is configured.
- The interactive login goes through WAM, parented to a hidden form so the dialog doesn't get lost behind other windows.
- Jobs are independent. The calendar fetch for a job is unaffected by its Teams login state.

## 9. Code layout

The single-file version is kept as git tag `single-file`. The app is now `MeetingAlarm.slnx`:

```
src/MeetingAlarm/            WinExe, net10.0-windows, single-file publish (incl. WAM native lib)
  Program.cs  Config.cs  AlarmContext.cs  IcsCalendar.cs  Autostart.cs  ColorParser.cs  Localization.cs
  Placement.cs (anchor/stack math)  PopupBase.cs  MeetingPopup.cs  ChatPopup.cs
  Teams/ChatPoller.cs (MSAL + Graph via HttpClient)  Teams/ChatCounter.cs (pure counting)
  Teams/ChatState.cs (acks)  Teams/TeamsLink.cs (msteams: deep links)
tests/MeetingAlarm.Tests/    xunit: counting, placement, colors, localization, Teams links
```

Graph is called with plain `HttpClient` + `JsonDocument`, not the Graph SDK: four GET calls don't justify that dependency.

## 10. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | A tenant blocks consent for `Chat.Read` for every client ID | Fallback: read Teams' Windows toast notifications via `UserNotificationListener`. It needs package identity (a sparse MSIX), only sees what Teams notified about, and gets nothing while Teams is focused or in Do Not Disturb. Build this only if needed. |
| R2 | Conditional Access requires a compliant or joined device | WAM passes device state along. Otherwise there is no workaround; document it. |
| R3 | Token revoked (password change, policy) | "inloggen vereist" state (§8). |
| R4 | Graph throttling (429) | Honor `Retry-After`, back off that tenant only. |
| R5 | `viewpoint` not returned by `/me/chats` (verify first!) | Use `GET /chats/{id}` only for chats whose `lastMessagePreview` is newer than `AckedAt`. That keeps the extra calls to changed chats. |

## 11. Acceptance

- Two jobs with a tenant, both signed in. A 1:1 message in either tenant → the chat popup appears middle-right within
  `ChatPollSeconden` with the right name and count 1, and it flashes. A meeting popup at the same time appears bottom-right, independently.
- A second message → count 2, flashes again.
- `✓` → the row disappears. Last row gone → the popup hides.
- Restart the app → acked chats stay hidden, and unacked chats show the same counts as before.
- A new message after the ack → the row comes back with count 1.
- Reading the chat in Teams (without acking) → the row disappears on the next poll.
- First run with an existing backlog: counts match what Teams shows as unread.
- Sending a message myself → that chat is auto-acked.
- Changing `ChatPositie` / `MeetingPositie` in `config.json` → open popups move without a restart.
- One tenant's token revoked → that job shows "inloggen vereist". The other job and all calendars keep working.
- A config without Teams fields → behaves exactly like today.
