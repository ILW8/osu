# Tournament multiplayer IPC file producer

**Status:** Design approved 2026-04-17
**Owner:** ILW8

## 1. Motivation

`osu.Game.Tournament` currently *consumes* IPC text files produced by the legacy stable client (`ipc.txt`, `ipc-scores.txt`, `ipc-state.txt`, `ipc-channel.txt` in the stable install directory). External streaming overlays and scoreboards historically consume those same files by periodic re-reading.

The tournament overlay is moving off stable toward direct multiplayer-room spectating (`MultiplayerMatchIPCInfo`). When the overlay runs in multiplayer mode there is no stable client writing IPC files, so downstream tools lose their data source.

This spec adds a *producer* side: when the overlay runs in multiplayer-spectating mode, it writes live match state to a JSON file on disk, using the same "periodic re-read" contract that external tools are already built around.

## 2. Scope

**In scope:**
- A new component that serializes live multiplayer-room state to a single JSON file at a known path.
- Extension of `MultiplayerMatchIPCInfo` to expose the per-user gameplay data the writer needs.
- A user-configurable write interval (50–500 ms, default 250 ms) on the Setup screen.

**Out of scope:**
- Changes to the stable (file-based) IPC path. When `UseMultiplayerSpectating` is false, the writer is not instantiated at all; the stable client remains the producer.
- A line-based / stable-compatible format. Consumers of this new output will be written against the JSON schema defined here, not the legacy line-per-value format.
- Mods, chat channel, or tourney state fields. The operator uses a separate referee client for state; mods and channel are not required by the target consumers.

## 3. Output file

**Path:** `<osu-base>/tournaments/<current-tournament>/ipc/ipc.json`

The `ipc/` subdirectory is created on first write if missing. The tournament base is already available via the `Storage` dependency cached by `TournamentStorage`.

**Atomicity:** every write serializes to `ipc.json.tmp`, then replaces `ipc.json` via `File.Move(tempPath, finalPath, overwrite: true)`. Consumers polling the file never observe a partially written document.

## 4. JSON schema

```json
{
  "connected": true,
  "roomId": 12345,
  "beatmapId": 87654,
  "scores": { "team1": 1234567, "team2": 1200000 },
  "users": [
    {
      "userId": 9876,
      "teamId": 1,
      "score": 612345,
      "combo": 128,
      "accuracy": 0.9821,
      "hits": { "great": 456, "ok": 7, "meh": 1, "miss": 2 },
      "gameplayTimeMs": 47320
    }
  ]
}
```

Field semantics:

| Field | Type | Notes |
| --- | --- | --- |
| `connected` | bool | `true` iff `MultiplayerMatchIPCInfo.IsConnected` is true. |
| `roomId` | long \| null | `ConnectedRoomId`, or the last-known room ID when disconnected after a prior connection (see §6). Null if never connected. |
| `beatmapId` | int \| null | Online ID of the currently selected beatmap, or null if none. |
| `scores.team1` / `scores.team2` | long | Team totals, mirror `Score1` / `Score2` bindables. |
| `users[]` | array | One entry per watched user. Order not guaranteed. |
| `users[].userId` | int | osu! user ID. |
| `users[].teamId` | int | 1 or 2 — the JSON output uses 1-indexed teams throughout so consumers never see the internal 0/1 `TeamVersusUserState.TeamID` value. Computed as `TeamID + 1`. Users without a team state are omitted. |
| `users[].score` | long | `FrameHeader.TotalScore` of the most recent frame. |
| `users[].combo` | int | `FrameHeader.Combo` of the most recent frame. |
| `users[].accuracy` | double | `FrameHeader.Accuracy`, 0.0–1.0. |
| `users[].hits` | object | Map of lowercase `HitResult` enum name → count. Keys vary by ruleset (e.g. `great`/`ok`/`meh`/`miss` for osu!std). |
| `users[].gameplayTimeMs` | double | `bundle.Frames.Last().Time` of the most recent bundle. |

Serialization uses the project's standard JSON serializer with lowercase property names. No schema-version field for v1 — additive changes are expected to stay backward-compatible.

## 5. Component design

### 5.1 Changes to `MultiplayerMatchIPCInfo`

Today `MultiplayerMatchIPCInfo` tracks only `Dictionary<int, long> userScores` (score only). To avoid a second frame subscription in the writer, extend this dictionary to carry all five per-user fields.

**Add:** an internal record type. `osu.Game.Tournament` already has `InternalsVisibleTo("osu.Game.Tournament.Tests")` and `InternalsVisibleTo("osu.Game.Tournament.Tests.Dynamic")` in `AssemblyInfo.cs`, so `internal` is the right visibility — tests can reach it, but it stays out of the project's public surface.

```csharp
internal readonly record struct UserGameplayState(
    long Score,
    int Combo,
    double Accuracy,
    IReadOnlyDictionary<HitResult, int> Hits,
    double GameplayTimeMs);
```

**Replace:** `Dictionary<int, long> userScores` with `Dictionary<int, UserGameplayState> userStates`.

**Extend:** `onNewFrames` to populate all five fields from the same `FrameDataBundle`:

```csharp
Schedule(() =>
{
    var header = bundle.Header;
    double gameplayTime = bundle.Frames.Count > 0 ? bundle.Frames[^1].Time : 0;

    userStates[userId] = new UserGameplayState(
        header.TotalScore,
        header.Combo,
        header.Accuracy,
        header.Statistics,
        gameplayTime);

    updateTeamScores();
});
```

**Expose:** `internal IReadOnlyDictionary<int, UserGameplayState> UserStates => userStates;` for the writer to read. Same-assembly, so `internal` is sufficient.

**Update:** `updateTeamScores()` reads `.Score` off the record (mechanical change, same logic).

**Reset points:** the existing resets in `Disconnect()` and `onLoadRequested` (which today clear `userScores` entries to 0) continue to work — they now clear `userStates` entries to a default `UserGameplayState` or remove them.

### 5.2 New class `MultiplayerIPCWriter`

```
osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs
```

A `Component` that polls `MultiplayerMatchIPCInfo` at the configured interval and writes JSON.

**Resolved dependencies:**

- `MultiplayerMatchIPCInfo` (source of truth for bindables and per-user state)
- `MultiplayerClient` (for `Room.Users` → team IDs)
- `Storage` (tournament storage, from `TournamentGameBase`)
- `LadderInfo` (for the configurable interval)

**Lifecycle:**

1. `[BackgroundDependencyLoader] load()`:
   - Ensure `ipc/` subdirectory exists.
   - Perform one initial write of the disconnected snapshot so the file is present before any consumer polls (§6). Called from `load()` so it runs before any tick and before any `Connect()` could fire.
   - Schedule the first polling tick via `Scheduler.AddDelayed(writeTick, intervalMs, repeat: true)`.
   - Subscribe to `LadderInfo.IPCWriteIntervalMilliseconds.ValueChanged` to cancel the existing scheduled delegate and reschedule at the new interval.
2. `writeTick()`:
   - Build an `IPCSnapshot` record (see §5.3) from current state.
   - Compare to the last-written snapshot. If equal, skip.
   - Otherwise serialize and atomic-write, then store as last-written.
3. `Dispose(bool)`:
   - Cancel scheduled delegates.
   - One final write so the file reflects the final state on clean shutdown.

**No direct frame subscription.** The writer reads per-user data out of `MultiplayerMatchIPCInfo.UserStates` and joins with `multiplayerClient.Room.Users` to attach `teamId`.

### 5.3 Snapshot record

```csharp
internal readonly record struct IPCSnapshot(
    bool Connected,
    long? RoomId,
    int? BeatmapId,
    long Team1Score,
    long Team2Score,
    ImmutableArray<IPCUserSnapshot> Users);

internal readonly record struct IPCUserSnapshot(
    int UserId,
    int TeamId,              // 1-indexed — see §4
    long Score,
    int Combo,
    double Accuracy,
    ImmutableDictionary<string, int> Hits,
    double GameplayTimeMs);
```

Records give free structural equality for the dirty-check. Using immutable collections avoids accidentally mutating the last-written snapshot via shared references.

### 5.4 Dirty check

Equality comparison on `IPCSnapshot` via the record's synthesized `Equals`. If the new snapshot equals the last-written one, skip the write — this avoids touching disk when nothing meaningful has changed between ticks (e.g. user is idle between rounds).

## 6. Disconnect / reconnect semantics

The writer owns a `lastConnectedSnapshot` field so it can preserve last-known values across a disconnect without modifying `MultiplayerMatchIPCInfo`'s own reset behavior (which the rest of the overlay UI depends on).

Behavior on each state transition:

| Transition | Behavior |
| --- | --- |
| Component load, no room ever connected | Write initial snapshot: `{connected: false, roomId: null, beatmapId: null, scores: {team1:0, team2:0}, users: []}`. The file exists from boot. |
| Connect to room | Clear `lastConnectedSnapshot`. Subsequent ticks serialize live `MultiplayerMatchIPCInfo` state directly, with `connected: true`. |
| Live updates during play | Normal tick: build snapshot from live state; if changed, write. |
| Disconnect | `MultiplayerMatchIPCInfo` resets its bindables to defaults (existing behavior; required by the rest of the overlay). On the next writer tick, `IsConnected` is false. If `lastConnectedSnapshot` is set (i.e. we were previously connected), serialize *that* snapshot with `connected` overridden to `false` — so overlays keep showing the last frame of the just-ended match. |
| Connect to new room | `lastConnectedSnapshot` was cleared on `Connect()`; new live data flows through normally. |

Implementation detail: on every tick where `IsConnected` is true, update `lastConnectedSnapshot` to the snapshot just built. On every tick where `IsConnected` is false:
- If `lastConnectedSnapshot` is set → write that snapshot with `Connected = false`.
- Otherwise → write the empty disconnected snapshot.

## 7. Configuration

Add to `LadderInfo`:

```csharp
public BindableInt IPCWriteIntervalMilliseconds { get; } =
    new BindableInt(250) { MinValue = 50, MaxValue = 500 };
```

(Serialization follows the same pattern as `VolumeMaster`, `MuteUISounds`, etc.)

**Setup screen:** add a slider control with label "IPC write interval" and min/max/step 50/500/10 ms. Visible only when `UseMultiplayerSpectating` is on. Include a small helper label showing the absolute output path so the operator can find the file.

## 8. Wiring

`TournamentGameBase.load()` already has two branches for the IPC source. Add one line in the multiplayer branch:

```csharp
if (ladder.UseMultiplayerSpectating.Value)
{
    var multiplayerIpc = new MultiplayerMatchIPCInfo();
    dependencies.CacheAs<MatchIPCInfo>(multiplayerIpc);
    dependencies.CacheAs(multiplayerIpc);
    ipc = multiplayerIpc;
}
else
{
    ipc = new FileBasedIPC();
    dependencies.CacheAs<MatchIPCInfo>(ipc);
}

Add(ipc);

if (ipc is MultiplayerMatchIPCInfo)
    Add(new MultiplayerIPCWriter());
```

No changes to the file-based branch.

## 9. Testing

**Unit tests (`MultiplayerIPCWriterTest`):**

- **Initial write:** writer component loads → `ipc.json` exists with `connected: false`, empty users, zero scores.
- **Schema round-trip:** given a populated `MultiplayerMatchIPCInfo` and `Room.Users`, serialize → deserialize → compare against a golden JSON fixture. Verifies field names, nesting, `hits` map keying.
- **Dirty skip:** two ticks with identical state produce exactly one write (count via a test `Storage` wrapper or by stamping file mtime).
- **Throttle interval:** set interval to 100 ms, advance scheduler by 1 s with continuous state changes → at most ~10 writes.
- **Interval change:** change `IPCWriteIntervalMilliseconds` at runtime → new scheduled delegate fires at the new cadence.
- **Disconnect preserves last frame:** populate → disconnect → next tick writes `connected: false` with roomId/beatmapId/scores/users still populated.
- **New connection clears last-frame:** populate room A → disconnect → connect to room B with no live data yet → tick writes `connected: true, roomId: B, users: []` (no room-A users bleeding through).
- **Atomic write:** the temp file is written and renamed (verify via a test storage that records operations).

**Manual smoke test (documented in the spec):**

1. Configure multiplayer spectating, point the overlay at a live test room.
2. Confirm `ipc.json` appears under `<tournament>/ipc/` with `connected: true` and live data.
3. Start a round, watch per-user scores/combo/accuracy update.
4. Disconnect from the room via the overlay. Verify `connected: false` flips and the last-round values remain.
5. Connect to a different room. Verify old values are cleared before the new room's data populates.
6. Verify consumers polling the file never see invalid JSON (atomic write).

## 10. Open questions

None. All design decisions resolved during brainstorming on 2026-04-17.
