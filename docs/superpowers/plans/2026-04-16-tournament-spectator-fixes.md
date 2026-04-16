# Tournament Spectator Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix critical round-transition bugs, remove dead code, and improve robustness of the tournament multiplayer spectator implementation.

**Architecture:** `TournamentGameplayDisplay` needs to become round-aware by reacting to multiplayer lifecycle events (LoadRequested/GameplayAborted) so it tears down and rebuilds PlayerAreas between rounds. `TournamentSpectatorDisplay` is dead code and gets deleted. Exception handling in `MultiplayerMatchIPCInfo` gets tightened up. Player quit/fail gets visual feedback.

**Tech Stack:** C#, osu-framework, NUnit, osu.Game.Tournament

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `osu.Game.Tournament/Components/TournamentSpectatorDisplay.cs` | **Delete** | Dead code removal |
| `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs` | **Modify** | Round-transition teardown, quit visual feedback |
| `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs` | **Modify** | Tighten exception handling |

---

### Task 1: Delete TournamentSpectatorDisplay

This file is unused placeholder code. No references exist outside its own file.

**Files:**
- Delete: `osu.Game.Tournament/Components/TournamentSpectatorDisplay.cs`

- [ ] **Step 1: Verify no references exist**

Run:
```bash
cd /c/Users/daohe/RiderProjects/osu && grep -r "TournamentSpectatorDisplay" --include="*.cs" -l
```

Expected: Only `TournamentSpectatorDisplay.cs` itself. If other files reference it, do not delete — update the plan.

- [ ] **Step 2: Delete the file**

```bash
rm osu.Game.Tournament/Components/TournamentSpectatorDisplay.cs
```

- [ ] **Step 3: Build to confirm no breakage**

Run:
```bash
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj --no-restore
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add osu.Game.Tournament/Components/TournamentSpectatorDisplay.cs
git commit -m "remove unused TournamentSpectatorDisplay placeholder"
```

---

### Task 2: Make TournamentGameplayDisplay round-aware

This is the critical fix. Currently, `PlayerArea`s created during round 1 persist into round 2. `PlayerArea.LoadScore()` throws `InvalidOperationException` if called twice, so gameplay silently fails to load for subsequent rounds. The fix: subscribe to `multiplayerClient.LoadRequested` and `multiplayerClient.GameplayAborted` to call `teardownGameplay()` between rounds.

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`

- [ ] **Step 1: Add event subscriptions in LoadComplete**

In `TournamentGameplayDisplay.cs`, add subscriptions to the multiplayer client's round lifecycle events. These events fire when a new round is about to start (LoadRequested) or is cancelled (GameplayAborted), both of which require clearing old gameplay state.

In the `LoadComplete()` method, after the existing `multiplayerIpc.IsConnected.BindValueChanged(...)` block, add:

```csharp
multiplayerClient.LoadRequested += onLoadRequested;
multiplayerClient.GameplayAborted += onGameplayAborted;
```

- [ ] **Step 2: Add the handler methods**

Add these two methods to the class. `onLoadRequested` fires when the host starts a new round — tear down all old gameplay so fresh PlayerAreas can be created when players enter Playing state. `onGameplayAborted` fires if the round is cancelled mid-load.

```csharp
private void onLoadRequested()
{
    Schedule(teardownGameplay);
}

private void onGameplayAborted(GameplayAbortReason _)
{
    Schedule(teardownGameplay);
}
```

Add the required using at the top of the file:

```csharp
using osu.Game.Online.Multiplayer;
```

(Note: this using already exists — verify and skip if so.)

- [ ] **Step 3: Unsubscribe in Dispose**

In the existing `Dispose(bool isDisposing)` override, add cleanup for the new subscriptions. Add inside the existing null-safety block — the method currently only disposes the `realmSubscription`. Add the multiplayerClient unsubscription:

Replace the current `Dispose` method:

```csharp
protected override void Dispose(bool isDisposing)
{
    base.Dispose(isDisposing);
    realmSubscription?.Dispose();
}
```

With:

```csharp
protected override void Dispose(bool isDisposing)
{
    base.Dispose(isDisposing);
    realmSubscription?.Dispose();

    if (multiplayerClient.IsNotNull())
    {
        multiplayerClient.LoadRequested -= onLoadRequested;
        multiplayerClient.GameplayAborted -= onGameplayAborted;
    }
}
```

This requires adding a using for `ObjectExtensions` if not already present:

```csharp
using osu.Framework.Extensions.ObjectExtensions;
```

(Note: this using already exists — verify and skip if so.)

- [ ] **Step 4: Build**

Run:
```bash
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj --no-restore
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Components/TournamentGameplayDisplay.cs
git commit -m "tear down gameplay on LoadRequested/GameplayAborted for round transitions"
```

---

### Task 3: Add visual feedback for player quit

When a player quits mid-map, the reference `MultiSpectatorScreen` fades their area to grey. The tournament implementation currently shows no visual change. This task adds the same fade treatment.

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`

- [ ] **Step 1: Split onPlayerFinished into state-specific handlers**

Currently all three end states (Passed, Failed, Quit) call the same `onPlayerFinished` method. Replace the switch cases in `onUserStateChanged` to differentiate Quit from the others.

Replace the existing switch block in `onUserStateChanged`:

```csharp
switch (newState.State)
{
    case SpectatedUserState.Playing:
        tryStartGameplay(userId);
        break;

    case SpectatedUserState.Passed:
        onPlayerFinished(userId);
        break;

    case SpectatedUserState.Failed:
        onPlayerFinished(userId);
        break;

    case SpectatedUserState.Quit:
        onPlayerFinished(userId);
        break;
}
```

With:

```csharp
switch (newState.State)
{
    case SpectatedUserState.Playing:
        tryStartGameplay(userId);
        break;

    case SpectatedUserState.Passed:
    case SpectatedUserState.Failed:
        onPlayerFinished(userId);
        break;

    case SpectatedUserState.Quit:
        onPlayerFinished(userId);
        onPlayerQuit(userId);
        break;
}
```

- [ ] **Step 2: Add the onPlayerQuit method**

Add this method after `onPlayerFinished`:

```csharp
private void onPlayerQuit(int userId)
{
    if (playerAreas.TryGetValue(userId, out var area))
        area.FadeColour(new Colour4(68, 68, 68, 255), 400, Easing.OutQuint);
}
```

This matches the reference `MultiSpectatorScreen`'s `colours.Gray4` (#444) without needing to resolve `OsuColour` via DI. No new usings needed — `Colour4`, `Easing` are already imported via `osu.Framework.Graphics`.

- [ ] **Step 3: Build**

Run:
```bash
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj --no-restore
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add osu.Game.Tournament/Components/TournamentGameplayDisplay.cs
git commit -m "fade player area to grey when spectated player quits"
```

---

### Task 4: Replace bare catch blocks with targeted handling

`MultiplayerMatchIPCInfo.updateBeatmapFromRoom()` and `updateModsFromRoom()` both swallow all exceptions silently when accessing `CurrentPlaylistItem`. This makes debugging difficult. Replace with a conditional check since `CurrentPlaylistItem` throws when the playlist is empty.

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`

- [ ] **Step 1: Fix updateBeatmapFromRoom**

Replace the try-catch in `updateBeatmapFromRoom()`:

```csharp
MultiplayerPlaylistItem currentItem;

try
{
    currentItem = multiplayerClient.Room.CurrentPlaylistItem;
}
catch
{
    return;
}
```

With a conditional check on the playlist:

```csharp
if (multiplayerClient.Room.Playlist.Count == 0)
    return;

var currentItem = multiplayerClient.Room.CurrentPlaylistItem;
```

- [ ] **Step 2: Fix updateModsFromRoom**

Apply the same replacement in `updateModsFromRoom()`. Replace:

```csharp
MultiplayerPlaylistItem currentItem;

try
{
    currentItem = multiplayerClient.Room.CurrentPlaylistItem;
}
catch
{
    return;
}
```

With:

```csharp
if (multiplayerClient.Room.Playlist.Count == 0)
    return;

var currentItem = multiplayerClient.Room.CurrentPlaylistItem;
```

- [ ] **Step 3: Verify the Playlist property exists and is a collection**

Run:
```bash
cd /c/Users/daohe/RiderProjects/osu && grep -n "Playlist" osu.Game/Online/Multiplayer/MultiplayerRoom.cs | head -10
```

Verify that `MultiplayerRoom.Playlist` is a list/collection with a `Count` property. If it uses a different API, adjust accordingly.

- [ ] **Step 4: Build**

Run:
```bash
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj --no-restore
```

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs
git commit -m "replace bare catch blocks with playlist count checks"
```

---

### Task 5: Remove redundant beatmap download path

`updateBeatmapFromRoom()` triggers `ensureBeatmapDownloadedById(beatmapId)` unconditionally at line 413, even when the beatmap was already found in the round pool. This causes a redundant API lookup. Move the download call into the else branch where it's actually needed.

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`

- [ ] **Step 1: Move ensureBeatmapDownloadedById into the else branch**

In `updateBeatmapFromRoom()`, the current structure is:

```csharp
if (existing != null)
{
    Beatmap.Value = existing.Beatmap;
}
else
{
    // Fall back to API lookup.
    Task.Run(async () =>
    {
        var apiBeatmap = await beatmapLookupCache.GetBeatmapAsync(beatmapId).ConfigureAwait(false);

        Schedule(() =>
        {
            if (lastBeatmapId == beatmapId && apiBeatmap != null)
                Beatmap.Value = new TournamentBeatmap(apiBeatmap);
        });

        // Ensure the beatmap is downloaded locally for gameplay rendering.
        if (apiBeatmap != null)
            ensureBeatmapDownloaded(apiBeatmap);
    });
}

// Also ensure the beatmap is downloaded for maps from the pool.
ensureBeatmapDownloadedById(beatmapId);
```

Replace with:

```csharp
if (existing != null)
{
    Beatmap.Value = existing.Beatmap;
    // Ensure the pool beatmap is downloaded locally for gameplay rendering.
    ensureBeatmapDownloadedById(beatmapId);
}
else
{
    // Fall back to API lookup.
    Task.Run(async () =>
    {
        var apiBeatmap = await beatmapLookupCache.GetBeatmapAsync(beatmapId).ConfigureAwait(false);

        Schedule(() =>
        {
            if (lastBeatmapId == beatmapId && apiBeatmap != null)
                Beatmap.Value = new TournamentBeatmap(apiBeatmap);
        });

        // Ensure the beatmap is downloaded locally for gameplay rendering.
        if (apiBeatmap != null)
            ensureBeatmapDownloaded(apiBeatmap);
    });
}
```

The key change: `ensureBeatmapDownloadedById` stays in the `if (existing != null)` branch (pool maps still need local download for gameplay), and the `else` branch handles download via `ensureBeatmapDownloaded(apiBeatmap)` which it already does. This eliminates the redundant API lookup in the else path (previously both `ensureBeatmapDownloaded` and `ensureBeatmapDownloadedById` would run).

- [ ] **Step 2: Build**

Run:
```bash
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj --no-restore
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs
git commit -m "avoid redundant beatmap API lookup when map is in pool"
```

---

## Execution Order

Tasks 1-5 are independent and can be executed in any order. However, the recommended order is as listed because:
- Task 1 (delete dead code) is trivial and clears noise
- Task 2 (round-transition fix) is the highest-priority bug fix
- Task 3 (quit feedback) builds on the code touched in Task 2
- Tasks 4-5 are cleanup in `MultiplayerMatchIPCInfo`

## Known Limitations Not Addressed

These were identified in the review but are intentionally deferred:

1. **One player per team side** — This is a fundamental layout decision for the tournament overlay's constrained chroma area. Expanding to N players per team requires a grid layout redesign and is out of scope for a bug-fix pass.

2. **No automatic reconnection** — The `MultiplayerClient` has built-in SignalR reconnection, but a full disconnect requires manual re-join. This is acceptable for tournament operator workflows where manual control is preferred.

3. **`scheduleOnUpdateThread` async-void pattern** — Technically works correctly due to the TaskCompletionSource propagation. The risk is low and refactoring it would touch the connection flow which is otherwise working.
