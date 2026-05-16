# Tournament Client Remote Control (HTTP)

**Status:** design approved, awaiting implementation plan
**Date:** 2026-05-16
**Branch context:** `feature/lazer-tournament-spectator`

## Goal

Expose a small HTTP API embedded in the tournament client so an external controller (Bitfocus Companion) can switch screens, manage the multiplayer connection, accept/dismiss invites, increment match scores, and poll basic state.

## Motivation

During a broadcast the operator's keyboard is often a separate machine running Companion. Today every control surface in `TournamentSceneManager` and `MultiplayerRoomConnectionControls` is reachable only through the on-screen left panel. A prior implementation used an embedded WebSocket server; this design replaces that with HTTP because polled feedback is sufficient, the wire protocol is simpler, and Companion's built-in "Generic HTTP Request" module covers it without any custom Companion-side module.

## Non-goals

- Auth / TLS. v1 binds to a configurable address (default `127.0.0.1`) and exposes no auth surface. Operator is expected to keep the bind address local or run on a trusted LAN.
- Push feedback / WebSocket. Companion polls `GET /status`; lag of a Companion poll interval (typically 500 ms–2 s) is acceptable.
- A Setup-screen UI for configuring the feature. v1 is configured by editing `tournament.ini`. A UI toggle is a follow-up.
- Endpoints for operations not yet wired up by the existing left-panel UI. The remote surface mirrors what an operator can already click; adding new game-state operations is out of scope.

## Architecture

New component `TournamentRemoteControl` lives in `osu.Game.Tournament` (suggested path: `osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs`). It is a `Component` (no visual presence) added to the `TournamentGame` drawable graph after `BracketLoadTask` resolves, so its `[BackgroundDependencyLoader]` can resolve all needed caches.

Resolved dependencies:

- `TournamentSceneManager` — calls `SetScreen(Type)` to change the active screen.
- `LadderInfo` — reads `CurrentMatch` and mutates `Team1Score` / `Team2Score`.
- `MatchIPCInfo` — when the resolved instance is a `MultiplayerMatchIPCInfo`, the multiplayer endpoints are active and call `Connect` / `Disconnect` / `Reconnect` / `AcceptPendingInvite` / `DismissPendingInvite` / `SetPendingInvite`. When the IPC is `FileBasedIPC`, the multiplayer endpoints respond `503 Service Unavailable`.

The component owns:

- One `System.Net.HttpListener`.
- One dedicated background thread that loops on the listener's `GetContext()`.
- A `CancellationTokenSource` used for shutdown.

All mutations of game state are dispatched onto the framework update thread via the component's `Scheduler`. Response writing happens on the listener thread. The update thread is never blocked on network I/O.

## Configuration

Extend `osu.Game.Tournament/Configuration/TournamentConfigManager.cs`:

```csharp
public enum StorageConfig
{
    CurrentTournament,
    RemoteControlEnabled,
    RemoteControlBindAddress,
    RemoteControlPort,
}
```

Defaults (added to `InitialiseDefaults`):

| Key | Type | Default | Notes |
|---|---|---|---|
| `RemoteControlEnabled` | `bool` | `false` | Opt-in. |
| `RemoteControlBindAddress` | `string` | `"127.0.0.1"` | Set to `0.0.0.0` for LAN. |
| `RemoteControlPort` | `int` | `7270` | Configurable; chosen to avoid common dev-server conflicts. |

v1 is configured by editing `tournament.ini`. A Setup-screen UI is a documented follow-up but not part of this spec.

## Command surface

All command endpoints use `POST`; status uses `GET`. Successful actions return `200` with `{"ok":true}`. Errors return a JSON body of shape `{"ok":false,"error":"<message>"}` with a status code chosen by category (see "Response codes" below).

### Screen switching

```
POST /screen/{name}
```

`{name}` is one of:

| name | Type |
|---|---|
| `setup` | `SetupScreen` |
| `schedule` | `ScheduleScreen` |
| `ladder` | `LadderScreen` |
| `ladder-editor` | `LadderEditorScreen` |
| `team-editor` | `TeamEditorScreen` |
| `round-editor` | `RoundEditorScreen` |
| `showcase` | `ShowcaseScreen` |
| `mappool` | `MapPoolScreen` |
| `teamintro` | `TeamIntroScreen` |
| `seeding` | `SeedingScreen` |
| `drawings` | `DrawingsScreen` |
| `gameplay` | `GameplayScreen` |
| `teamwin` | `TeamWinScreen` |

Unknown name → `400`.

### Multiplayer connection

```
POST /multiplayer/connect
    body  : JSON { "roomId": <long>, "password": <string|null> }
    or query: ?roomId=123&password=...

POST /multiplayer/disconnect
POST /multiplayer/reconnect
POST /multiplayer/invite/accept
POST /multiplayer/invite/dismiss
```

`POST /multiplayer/connect` requires a `roomId`; if missing or unparseable → `400`. Body JSON takes precedence over query parameters when both are present.

All multiplayer endpoints respond `503` when the resolved `MatchIPCInfo` is not a `MultiplayerMatchIPCInfo`.

`/multiplayer/disconnect` mirrors the hold-to-confirm UI button's underlying action — the remote call bypasses the hold confirmation by design. The UI's hold gesture exists to prevent stray clicks; a deliberate HTTP call is itself a deliberate action.

State preconditions are enforced and return `409` on mismatch:

- `connect` when already connected.
- `disconnect` / `reconnect` when not connected.
- `invite/accept` and `invite/dismiss` when there is no pending invite.

### Match score

```
POST /match/score/red/increment
POST /match/score/blue/increment
```

Increments `LadderInfo.CurrentMatch.Value.Team1Score` (red) or `.Team2Score` (blue). If `CurrentMatch` is null or either score bindable is null, the increment treats the starting value as `0` and writes `1` (matching the existing in-app behavior at `GameplayScreen.cs:349-351`). If `CurrentMatch.Value` is null entirely, respond `409` with `"no current match"`.

### Status

```
GET /status
```

Response shape:

```json
{
    "currentScreen": "GameplayScreen",
    "multiplayer": {
        "available": true,
        "connected": true,
        "roomId": 12345,
        "pendingInvite": { "roomId": 67, "inviter": "name" },
        "tourneyState": "Playing"
    },
    "match": {
        "team1Score": 2,
        "team2Score": 1,
        "team1Acronym": "RED",
        "team2Acronym": "BLU"
    }
}
```

Field rules:

- `currentScreen` — the simple type name of the currently-shown screen, or `null` if none is active.
- `multiplayer.available` — `false` when running with `FileBasedIPC`; in that case the object is just `{ "available": false }` and no other multiplayer fields appear.
- `multiplayer.connected` / `roomId` — read from `MultiplayerMatchIPCInfo.IsConnected` / `ConnectedRoomId`.
- `multiplayer.pendingInvite` — `null` when there is no pending invite; otherwise the `roomId` and `inviter` name.
- `multiplayer.tourneyState` — the current `TourneyState` enum value name.
- `match` — `null` when `CurrentMatch.Value` is null. Acronyms read from `Team1.Value.Acronym.Value` / `Team2.Value.Acronym.Value`, with `null` substituted when a team is unset.

### Response codes

| Code | Meaning |
|---|---|
| `200` | Success. Body: `{"ok":true}` or the status JSON. |
| `400` | Bad request: unknown screen name, missing `roomId`, malformed JSON. |
| `404` | Unknown route. |
| `405` | Wrong method (e.g. `GET /screen/setup`). |
| `409` | State conflict: action invalid for current state. |
| `500` | Unexpected exception while dispatching. |
| `503` | Multiplayer endpoint called when not running multiplayer IPC. |
| `504` | Update thread did not complete the action within the dispatch timeout. |

### Content type

Request bodies, when present, must be `application/json`. Responses are always `application/json; charset=utf-8`. CORS is not configured — Companion's HTTP module does not need it.

## Threading model

1. Listener thread receives a request via `HttpListener.GetContext()`.
2. Route + body parsing happens on the listener thread.
3. The handler creates a `TaskCompletionSource<HttpResult>` and schedules the game-state mutation via the component's `Scheduler.Add(() => { …; tcs.SetResult(...) })`. The lambda must not throw — any failure is captured into the TCS as an error result.
4. The listener thread awaits the TCS with a 2-second timeout (`Task.Wait(2000)`). On timeout it writes `504` and abandons the TCS (the scheduled lambda still runs but its result is discarded).
5. The response is written on the listener thread.

`GET /status` follows the same pattern but the scheduled lambda's only job is to snapshot all fields into an immutable record; JSON serialization runs on the listener thread.

This pattern avoids blocking the framework update thread on I/O and avoids racing on bindable access from a non-update thread.

### Async multiplayer actions

`MultiplayerMatchIPCInfo.Connect` / `Disconnect` / `Reconnect` return `Task` and complete on a network round-trip. The scheduled lambda calls the method and chains `ContinueWith` to complete the TCS with `200` on success or `500` (carrying the exception message) on failure. If the 2-second wait elapses before the task completes, the HTTP response is `504` while the operation itself continues in the background; the caller can re-check via `GET /status`. This matches the existing UI's fire-and-forget convention while giving HTTP callers an explicit "operation took too long, poll for outcome" signal.

## Lifecycle

- `LoadComplete()`:
  - Read `RemoteControlEnabled`. If `false`, the component remains loaded but inert; no listener is created.
  - If `true`, call `Start()`.
- `Start()`:
  - Create `HttpListener`, add prefix `http://{bindAddress}:{port}/`, call `Start()`.
  - Launch the listener thread.
  - On `HttpListenerException` (port in use, ACL refused, etc.): log via `Logger.Error`, dispose the listener, and continue running without remote control. The tournament app must never crash because of a bind failure.
- `Dispose()`:
  - Signal the cancellation token.
  - Call `HttpListener.Close()` — this causes the blocking `GetContext()` in the listener thread to throw and exit cleanly.
  - Join the listener thread with a 1-second timeout. If the join times out, leave it; the process is exiting anyway.

## Error handling

- Bind failure → log `Error` with the listener exception, no further retry, no crash.
- Per-request exception during routing or scheduling → caught at the top of the listener loop, response `500` with the exception's `Message`, log via `Logger.Log(... LogLevel.Important)`. The listener keeps running.
- Listener shutdown — catch `ObjectDisposedException` and `HttpListenerException` with error code `995` (`WSA_OPERATION_ABORTED`) and exit the loop quietly.
- Schedule timeout (`504`) is logged at `LogLevel.Important` because it indicates the update thread is starved.

## Operator notes

These belong in a README or in code comments alongside the listener:

- **Default bind is `127.0.0.1`.** Companion must be running on the same machine in that configuration. For a remote control PC, change `RemoteControlBindAddress` to `0.0.0.0` (or a specific NIC IP).
- **Non-admin LAN binding on Windows.** Binding `HttpListener` to `0.0.0.0` from a non-elevated process requires a URL ACL reservation: `netsh http add urlacl url=http://+:7270/ user=Everyone`. Without it the listener will fail to start; the bind failure is logged but the app stays up.
- **Port choice.** Default `7270`. If conflicting with another tool, change `RemoteControlPort` in `tournament.ini`.

## Testing

Test fixture: `osu.Game.Tournament.Tests/RemoteControl/TournamentRemoteControlTest.cs`.

The fixture:

1. Spins up a `TournamentRemoteControl` against `127.0.0.1:0` (OS-chosen ephemeral port) inside a test scene that provides a real `TournamentSceneManager`, a `LadderInfo` with a synthetic `CurrentMatch`, and a stubbed `MatchIPCInfo`.
2. Uses `HttpClient` to drive each endpoint and asserts:
   - `POST /screen/{name}` for each name updates `TournamentSceneManager`'s current screen as observed via its public state.
   - `POST /match/score/red/increment` and `…/blue/increment` mutate `LadderInfo.CurrentMatch.Value.Team1Score` / `.Team2Score` (covering the null and non-null starting cases).
   - All `POST /multiplayer/*` endpoints respond `503` when the IPC is `FileBasedIPC`.
   - `POST /multiplayer/connect` returns `400` for missing `roomId`, parses JSON body, parses query string, and rejects body wins over query when both are present.
   - `GET /status` returns the documented JSON shape for both `FileBasedIPC` and `MultiplayerMatchIPCInfo` configurations.
   - Unknown routes return `404`; wrong method returns `405`.

A second fixture covers the `MultiplayerMatchIPCInfo` happy path against a stubbed multiplayer IPC, verifying that `Connect` / `Disconnect` / `Reconnect` / `AcceptPendingInvite` / `DismissPendingInvite` are invoked. If a stub is impractical, that part is exercised manually and the spec calls that out.

Bind-failure handling is exercised by starting two `TournamentRemoteControl` instances against a fixed port and asserting the second logs an error and does not throw.

## File touch list

New files:

- `osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs` — the component.
- `osu.Game.Tournament/RemoteControl/RemoteControlRouter.cs` — route dispatch (split for testability if it helps; can be inlined).
- `osu.Game.Tournament.Tests/RemoteControl/TournamentRemoteControlTest.cs` — fixture.

Modified files:

- `osu.Game.Tournament/Configuration/TournamentConfigManager.cs` — add three `StorageConfig` keys + defaults.
- `osu.Game.Tournament/TournamentGame.cs` — instantiate and add `TournamentRemoteControl` inside the `BracketLoadTask` continuation, alongside the other components.

## Open questions / follow-ups (not in this spec)

- Setup-screen UI for enable / bind / port.
- An optional `Bearer`-token header for LAN deployments. (Currently no auth.)
- Operator action feed (a `POST /screen/next` / `POST /screen/prev` style relative navigation) once the absolute API has shipped and we know what the operator workflow wants.
- Companion module: this design is usable with Companion's built-in Generic HTTP Request module. A dedicated Companion module that wraps the surface into named actions could ship later.
