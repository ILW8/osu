# Tournament Lobby Music — Design

## Problem

When the tournament overlay is connected to a multiplayer room as a spectator, the broadcast is silent during non-gameplay moments: the map pool screen, the gap between rounds, the waiting period after the host clicks Start, and so on. In the regular client's multiplayer flow the current map's track plays on loop while sitting in the room — we want the same behaviour for the tournament overlay so broadcasts aren't dead air between songs.

## Scope

In scope:

- Multiplayer-spectator mode only (`MultiplayerMatchIPCInfo`). The file-based IPC path keeps its existing behaviour (no local beatmap concept, no music).
- Plays the current room's beatmap, looping at the preview point, between rounds and on the map-pool / ladder / setup screens.
- Stops cleanly when gameplay begins so the in-broadcast gameplay rendering owns the audio.

Out of scope:

- No new operator toggle for "lobby music on/off". Existing master/music volume sliders in `GameplayScreen` already gate volume via `Audio.VolumeTrack`.
- No ducking/crossfading between lobby music and gameplay audio. Hard cut.
- No file-based IPC support.

## Trigger model

Music plays through the existing `MusicController` (already cached at `OsuGameBase` level) when **all** of:

- `MultiplayerMatchIPCInfo.IsConnected == true`
- `ipc.Beatmap.Value != null` AND the beatmap is locally available via `BeatmapManager`
- `ipc.State.Value ∈ { Idle, WaitingForClients }`

Music stops when any of those becomes false. The interesting transitions:

| From → To state              | Action      | Reason |
|------------------------------|-------------|--------|
| `Idle` → `WaitingForClients` | continue    | gameplay torn down, music still appropriate |
| `WaitingForClients` → `Playing` | stop      | `TournamentGameplayDisplay.masterClockContainer` will start the same track |
| `Playing` → `Ranking`        | (stay stopped) | master clock still running through results window |
| `Ranking` → `Idle` / `WaitingForClients` | resume | scheduled `TeardownGameplay()` in `GameplayScreen` has already stopped the master clock by then |
| any → disconnect             | stop        | no room context |

The rule could be summarised as "play iff State ∉ {Playing, Ranking} and we're connected with a known beatmap." Picking `Idle + WaitingForClients` explicitly (rather than "anything not in the stop set") keeps the gate conservative if future states are added to `TourneyState`.

## Component

New small drawable `TournamentLobbyMusic : Component`, added in `TournamentGameBase.readBracket`'s post-`Schedule` block, **only when `UseMultiplayerSpectating` is on**. Added as a sibling to the IPC, the same way `MultiplayerIPCWriter` is.

Resolved dependencies:

- `MultiplayerMatchIPCInfo` (concrete, not `MatchIPCInfo`)
- `MusicController`
- `BeatmapManager`
- `Bindable<WorkingBeatmap>` (the global beatmap bindable, cached by `OsuGameBase`)
- `RealmAccess` (to retry on beatmap import landing)

Behaviour:

1. On `ipc.Beatmap` change → look up local `WorkingBeatmap` via `beatmapManager.QueryBeatmap(b => b.OnlineID == ipc.Beatmap.Value.OnlineID)`. If found, `beatmapManager.GetWorkingBeatmap(localInfo)` → assign to the global `Bindable<WorkingBeatmap>`. Call `workingBeatmap.PrepareTrackForPreview(looping: true)` (mirrors `MultiplayerMatchSubScreen.applyLoopingToTrack`).
2. On `ipc.State`, `ipc.IsConnected`, or the resolved working-beatmap changing → re-evaluate. If the trigger model says "play", call `music.EnsurePlayingSomething()`. If it says "stop", call `music.Stop()`.
3. If the beatmap isn't yet locally available, do nothing — `MultiplayerMatchIPCInfo.ensureBeatmapDownloadedById` already queues the download. Subscribe to realm `BeatmapSetInfo` changes (mirroring `TournamentGameplayDisplay.beatmapsChanged`) and re-attempt the local lookup when an import lands matching the wanted `OnlineID`.
4. Don't fall back to a random track when no beatmap is set. The default `EnsurePlayingSomething` will call `NextTrack(allowProtectedTracks: true)` when the current track is a dummy device — we suppress that by *only* calling `EnsurePlayingSomething` after the global beatmap has been successfully set to the room's working beatmap.

## Interaction with existing audio

- `MuteUISounds` / per-name UI factory adjustment (`TournamentGameBase`): unaffected. The music track plays through `Audio.Tracks`, not `Audio.Samples`.
- Volume sliders (Master / Music / Effects) already bound to `Audio.Volume`, `Audio.VolumeTrack`, `Audio.VolumeSample`: unchanged. The user controls lobby music volume via the existing Music slider on the gameplay screen's control panel.
- `TournamentGameplayDisplay.teardownGameplay` already does `masterClockContainer?.Stop()` so the track is free for `MusicController` to play after teardown.
- During `Playing` and `Ranking` the master clock owns the track — `MusicController.Stop` on entering `Playing` ensures `DrawableTrack` is paused before the gameplay clock takes over.

## Alternatives considered

- **Manually drive a `Track` outside of `MusicController`.** Rejected: reimplements track loading, looping setup, mod-rate adjustments, and would conflict with the global beatmap bindable if anything else ever read it.
- **Hook into the gameplay master clock for between-rounds playback.** Rejected: `MasterGameplayClockContainer` is constructed per-round inside `TournamentGameplayDisplay.setupGameplayInfrastructure` and torn down between rounds; using it for lobby music would invert the lifecycle.
- **Place trigger logic inside `MultiplayerMatchIPCInfo` itself.** Rejected: IPC's job is data shape, not audio orchestration. Keeping it in a separate small component preserves the boundary and keeps the IPC unit-testable without a `MusicController` dependency.

## Risks

- **Beatmap not downloaded yet** — handled via realm subscription retry; in the worst case, lobby music doesn't play for the brief download window. Same UX as the existing gameplay rendering, which also waits on download.
- **Race with `MultiplayerIPCWriter` reads of `Beatmap.Value`** — none. `MultiplayerIPCWriter` reads `ipc.Beatmap` (the tournament-side bindable), not the global `Bindable<WorkingBeatmap>`. They're separate bindables.
- **Multiplayer client framework bumps** — the trigger model only depends on `TourneyState` enum values and `MultiplayerMatchIPCInfo`'s public bindables, which are owned in this repo.

## Test surface

Manual broadcast-style testing on a real or test multiplayer room:

- Connect to a room with a known map → music starts looping when state is `Idle`.
- Host clicks Start → state transitions to `WaitingForClients` (music continues) → `Playing` (music stops).
- After results auto-advance and `TeardownGameplay` fires → music resumes for the next round.
- Disconnect → music stops.
- Switching matches/rounds (different beatmap) → music swaps to the new beatmap on `ipc.Beatmap` change.

Unit-test-style coverage is awkward (relies on `MusicController` + `BeatmapManager` wiring); rely on manual verification plus a smoke test scene if a sensible one already exists for `MultiplayerMatchIPCInfo`.
