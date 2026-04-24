# Tournament spectator battle-royale grid

**Status:** Design approved 2026-04-24
**Owner:** dliu

## 1. Motivation

`TournamentGameplayDisplay` was built for 1v1 team-versus matches. It hard-codes a two-tile layout keyed off `TeamVersusUserState.TeamID` — team red on the left at 50% width, team blue on the right at 50% width — and actively rejects any additional user per side.

The tournament client needs to cover battle-royale-style matches with up to 8 players in a single lobby, where team identity is irrelevant and the operator wants to shrink the visible player set as players are eliminated so surviving players render larger on the broadcast.

Scope is deliberately narrow: this spec changes only the live gameplay grid inside `TournamentGameplayDisplay` plus one control-panel slider. Score UI, team model, bracket screens, and chroma-key outputs are untouched — scores are rendered downstream by an external graphics package that reads `ipc.json`, so the in-client `TournamentMatchScoreDisplay` does not need rework.

## 2. Scope

**In scope:**
- A unified N-tile grid (N = 2..8) replacing the existing 1v1 left/right split in `TournamentGameplayDisplay`.
- A new `TournamentPlayerGrid` component that handles responsive tile layout.
- A runtime operator-controlled "Visible players" slider (2..8) in the gameplay control panel that caps the number of rendered tiles.
- Snapshot-at-gameplay-start semantics: slot index per user ID is frozen once gameplay begins, so players don't shift positions when others leave mid-match.

**Out of scope:**
- `LadderInfo` fields / `bracket.json` persistence of the slider value — runtime-only `Bindable<int>`.
- Team score display (`TournamentMatchScoreDisplay`), chroma-key layout (`ChromaArea`), and bracket/team models — all unchanged. The external graphics package consumes `ipc.json` for scores.
- Changes to `MultiplayerIPCWriter` / `IPCSnapshot` output shape. `IPCSnapshot.Users` is already an N-user array; the writer is agnostic to grid presentation.
- Reuse of the main game's `PlayerGrid` (in `osu.Game/Screens/OnlinePlay/Multiplayer/Spectate/PlayerGrid.cs`) — we build a tournament-specific grid instead to avoid inheriting click-to-maximize behavior, which is inappropriate in a broadcast overlay.
- A manual per-player visibility mechanism (checkboxes to pick which players are hidden). Possible follow-up; not in v1.
- Handling of users who join the multiplayer room *after* gameplay begins — they simply receive no slot.

## 3. Component design

### 3.1 New — `TournamentPlayerGrid`

**Location:** `osu.Game.Tournament/Components/TournamentPlayerGrid.cs`

A `CompositeDrawable` that arranges 2–8 child tiles in a responsive grid sized to its parent.

**Public API:**

```csharp
public partial class TournamentPlayerGrid : CompositeDrawable
{
    public const int MAX_SLOTS = 8;
    public const int MIN_SLOTS = 2;

    public BindableInt Capacity { get; } = new BindableInt(MIN_SLOTS)
    {
        MinValue = MIN_SLOTS,
        MaxValue = MAX_SLOTS,
    };

    public void Add(Drawable tile, int slotIndex);
    public void Remove(int slotIndex);
    public void Clear();
}
```

**Semantics:**
- `Add(tile, slotIndex)` inserts a tile at a fixed slot index in `[0, MAX_SLOTS)`. Adding to an already-occupied slot is a programming error (assert).
- `Remove(slotIndex)` disposes and removes the tile at that slot. No-op if empty.
- `Capacity` caps the number of tiles that participate in the grid layout. Tiles at slot indices `>= Capacity` remain in the component tree (so their gameplay state is preserved) but have `Alpha = 0` and are excluded from size calculations.
- **Visible count** = `min(tile count, Capacity)` where "tile count" counts only slots with a tile present. Layout sizes are computed from visible count.

**Layout cases (visible count → grid dimensions):**

| Visible count | Grid |
| --- | --- |
| 2 | 2 × 1 |
| 3–4 | 2 × 2 |
| 5–6 | 3 × 2 |
| 7–8 | 4 × 2 |

These mirror the relevant subset of main-game `PlayerGrid` layouts. A visible count of 1 is not a supported state (slider min is 2), but if it occurs transiently (e.g. only one user ever started playing, slider is 2), it renders as one half of the 2×1 layout with an empty sibling half.

**Slot-to-cell mapping:** visible tiles are assigned to grid cells in ascending slot-index order, row-major. Missing slots (no tile added, or slot index `>= Capacity`) are skipped; the next present-and-visible slot takes the next cell. A tile, once added, is never removed during a single gameplay session (only on `teardownGameplay`), so mid-session cell positions are stable — the "no shift when a player leaves the room" invariant holds because we keep the tile in place and only fade it gray on quit.

**No click-to-maximize.** Unlike main-game `PlayerGrid`, tiles are not interactive.

### 3.2 Modified — `TournamentGameplayDisplay`

**File:** `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`

**Removed:**
- The team-side resolution block in `loadUserIntoPlayerArea` (current lines 266–278): team ID lookup, side-occupancy check, early-return-if-occupied.
- The anchored-container wrapper (current lines 288–296) that placed each `PlayerArea` inside a 0.5f-width left/right `Container`.

**Added:**

```csharp
public BindableInt VisibleSlotCount { get; } = new BindableInt(2)
{
    MinValue = TournamentPlayerGrid.MIN_SLOTS,
    MaxValue = TournamentPlayerGrid.MAX_SLOTS,
};

private readonly Dictionary<int, int> snapshottedSlots = new();
```

`playerAreasContainer` changes type from `Container` to `TournamentPlayerGrid`. The rest of the container hierarchy (master clock container → player areas container) is preserved so the master clock's `IGameplayClock` stays in the DI chain — `TournamentPlayerGrid` is a `CompositeDrawable`, same as `Container`, and can be nested identically.

**Snapshot in `setupGameplayInfrastructure`:** when the player-areas container is rebuilt, also rebuild the snapshot:

```csharp
snapshottedSlots.Clear();
if (multiplayerClient.Room != null)
{
    int slot = 0;
    foreach (var user in multiplayerClient.Room.Users)
    {
        if (slot >= TournamentPlayerGrid.MAX_SLOTS) break;
        snapshottedSlots[user.UserID] = slot++;
    }
}
```

The snapshot reads `room.Users` in the server's ordering (join order), truncated to `MAX_SLOTS`.

**Slot assignment in `loadUserIntoPlayerArea`:**

```csharp
if (!snapshottedSlots.TryGetValue(userId, out int slotIndex))
    return; // user joined after gameplay began — no slot available

if (playerAreas.ContainsKey(userId))
    return;

var playerArea = new PlayerArea(userId, syncManager.CreateManagedClock())
{
    RelativeSizeAxes = Axes.Both,
};
playerAreas[userId] = playerArea;
playerAreasContainer.Add(playerArea, slotIndex);
playerArea.LoadScore(gameplayState.Score);
// existing audio-adjustment binding logic unchanged
```

**Bind capacity in `load`/`LoadComplete`:** `VisibleSlotCount` is one-way-bound to `playerAreasContainer.Capacity`. Since `playerAreasContainer` is recreated in `setupGameplayInfrastructure`, the binding is established inside `setupGameplayInfrastructure` after the new grid is constructed.

**`teardownGameplay`:** clear `snapshottedSlots` alongside the other state.

**Preserved behaviors:**
- `onPlayerQuit` fading the tile to dim gray (existing `FadeColour` call) — unchanged.
- `performInitialSeek` averaging / outlier pruning — unchanged.
- Audio-source selection and muting — unchanged; it iterates `playerAreas.Values` and is agnostic to layout.

### 3.3 Modified — `GameplayScreen`

**File:** `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`

Add a `SettingsSlider<int>` labeled "Visible players" inside the existing `if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)` block, immediately after the multiplayer connection controls and before the volume controls. The slider is bound to `gameplayDisplay.VisibleSlotCount`:

```csharp
controlPanel.Add(new SettingsSlider<int>
{
    LabelText = "Visible players",
    Current = gameplayDisplay.VisibleSlotCount,
    KeyboardStep = 1,
});
```

The slider is only added when multiplayer spectating is active (same gating as the connection controls), so the stable-client flow is visually unchanged.

## 4. Data flow

1. Operator connects to a multiplayer room via the existing Connect button. `MultiplayerMatchIPCInfo.Connect(roomId)` populates `multiplayerClient.Room.Users` with all present users in server order.
2. First player reaches `LoadRequested`. `TournamentGameplayDisplay.setupGameplayInfrastructure` runs: new `TournamentPlayerGrid` created, `VisibleSlotCount` bound to its `Capacity`, `snapshottedSlots` populated from `room.Users` (truncated to 8).
3. Each user's `SpectatorState` transitions to `Playing`. `loadUserIntoPlayerArea` looks up the user's snapshot slot, creates a `PlayerArea`, adds it to the grid at that slot.
4. Grid's `Update()` computes visible count (number of present tiles whose slot index `<` `Capacity`) and resizes tiles to fit the matching layout case.
5. Operator drags the slider. `VisibleSlotCount` → `TournamentPlayerGrid.Capacity` → relayout. Tiles at slots `>= Capacity` fade to `Alpha = 0`; tiles at slots `< Capacity` animate to new sizes.
6. A user quits (`SpectatedUserState.Quit`) → existing `onPlayerQuit` fades their tile gray. Tile stays in its slot.
7. A user leaves the multiplayer room → no effect on the grid (snapshot is frozen). Their tile, if present, continues to show whatever state `PlayerArea` settles into.
8. `teardownGameplay` fires on disconnect / `LoadRequested` / `GameplayAborted` → snapshot cleared, grid replaced on next `setupGameplayInfrastructure` call.

## 5. Edge cases

| Situation | Behavior |
| --- | --- |
| `room.Users.Count > 8` at gameplay start | Snapshot truncated to first 8 by server order; users 9+ receive no slot. |
| `room.Users.Count == 1`, slider = 2 | One tile in slot 0, one empty cell in the 2×1 layout. |
| Slider lowered below currently-visible tile count | Highest-index tiles fade to `Alpha = 0` and are excluded from layout. Their gameplay clocks keep running. Audio-source selection (`updateAudioSource` / `isCandidateAudioSource`) is unchanged — it picks by clock state, not visibility — so the operator may still hear a hidden tile's player, same as current 1v1 behavior where audio isn't tied to a visual side. |
| Slider raised after elimination | Previously-hidden tiles fade back in at their original slot positions. |
| User rejoins after quitting (same user ID) | No new tile created — `loadUserIntoPlayerArea`'s existing `playerAreas.ContainsKey(userId)` guard returns early. The existing (gray-faded) tile remains in its slot. (Re-brightening a rejoined tile is out of scope; existing 1v1 code doesn't do it either.) |
| User in snapshot never starts playing | Their slot stays empty. Grid layout computes visible count from present tiles only, so the empty slot doesn't occupy a cell. |
| Snapshot taken with 2 users, a 3rd joins before `LoadRequested` | Since snapshot happens *inside* `setupGameplayInfrastructure` (triggered by `LoadRequested`), the 3rd user is included if they joined before the first `LoadRequested` fires. |
| Snapshot taken with 8 users, some leave immediately | Remaining users keep their slot indices. Grid still sized for their positions; gaps are treated as empty slots. |

## 6. Testing

### 6.1 Unit tests

- **`TournamentPlayerGridTest` (new, `osu.Game.Tournament.Tests/NonVisual/TournamentPlayerGridTest.cs`)**
  - Add 2..8 tiles at slots 0..N-1 with various capacities, assert visible count and that `Alpha` is 1 within capacity / 0 outside.
  - Add at specific slot, then `Remove`, assert tile is removed and visible count updates.
  - `Capacity` default is 2.
  - Assert: `Add` at already-occupied slot asserts; `Add` at out-of-range slot asserts.

### 6.2 Visual tests

- **`TestSceneTournamentPlayerGrid` (new, `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs`)**
  - Manual scene with colored placeholder `Box` tiles at slots 0..7, a slider bound to `Capacity`, and buttons to add/remove tiles at chosen slots.
  - Used to eyeball layout transitions when capacity changes.

- **`TestSceneGameplayScreen` (extend existing)**
  - Add a test that verifies the "Visible players" slider is present when `MultiplayerMatchIPCInfo` is the active IPC and absent when the stable `MatchIPCInfo` is active.

### 6.3 Manual broadcast test

- Connect the tournament client to a multiplayer room with 8 test accounts.
- Verify all 8 tiles render in the 4×2 layout.
- Drag the slider down to 4; verify tiles at slots 4..7 fade out and tiles 0..3 grow into the 2×2 layout.
- Have one test account quit; verify their tile fades gray but stays in its slot.
- Drag the slider back up; verify hidden tiles return.

## 7. File-level summary

| File | Change |
| --- | --- |
| `osu.Game.Tournament/Components/TournamentPlayerGrid.cs` | **New.** Responsive grid, `Capacity`, `Add`/`Remove`/`Clear`. |
| `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs` | Replace `playerAreasContainer` with `TournamentPlayerGrid`; add `VisibleSlotCount` + `snapshottedSlots`; rewrite slot-assignment logic in `loadUserIntoPlayerArea`; snapshot in `setupGameplayInfrastructure`. |
| `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs` | Add "Visible players" `SettingsSlider<int>` bound to `gameplayDisplay.VisibleSlotCount`, gated on multiplayer-spectating path. |
| `osu.Game.Tournament.Tests/NonVisual/TournamentPlayerGridTest.cs` | **New.** Unit tests for grid. |
| `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs` | **New.** Visual/manual test scene. |
| `osu.Game.Tournament.Tests/Screens/TestSceneGameplayScreen.cs` | Add slider-presence test. |

No changes to `LadderInfo`, `MultiplayerMatchIPCInfo`, `MultiplayerIPCWriter`, `IPCSnapshot`, or any bracket/team/score model.
