# Tournament spectator battle-royale grid implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 1v1 left/right split in `TournamentGameplayDisplay` with a unified 2–8 tile grid plus an operator-controlled "Visible players" slider, so the tournament client can spectate battle-royale-style multiplayer matches with dynamic tile-count reduction as players are eliminated.

**Architecture:** A new `TournamentPlayerGrid` composite handles responsive tile layout and capacity-based visibility. `TournamentGameplayDisplay` is retooled to take a one-time snapshot of `room.Users` at gameplay start, assign each user a fixed slot index, and insert tiles into the grid by slot. A runtime `SettingsSlider<int>` in `GameplayScreen`'s control panel binds to a new `VisibleSlotCount` bindable on the display.

**Tech Stack:** C# / osu-framework drawable hierarchy, NUnit + osu-framework test-scene pattern (`TournamentTestScene` → `OsuManualInputManagerTestScene`). Build: `dotnet build osu.sln`. Test assembly: `osu.Game.Tournament.Tests`.

**Scope note:** The spec listed a separate `TournamentPlayerGridTest.cs` in `NonVisual/`. That file is intentionally omitted — drawable layout behavior requires a running test-scene host, so all grid tests live in `TestSceneTournamentPlayerGrid` via `AddStep` / `AddAssert`. The spec also called for a slider-presence assertion in `TestSceneGameplayScreen`; automating that requires constructing a `MultiplayerMatchIPCInfo` in-test, which needs `MultiplayerClient` / `SpectatorClient` DI plumbing that no existing tournament test currently wires up. That assertion is therefore moved to the manual verification task (Task 7) rather than held as a blocker here. If the test-harness work is done later, it can be added as a follow-up.

**File structure:**

| File | Responsibility |
| --- | --- |
| `osu.Game.Tournament/Components/TournamentPlayerGrid.cs` | New. Responsive grid drawable: `Add(Drawable, int slotIndex)`, `Remove(int)`, `Clear()`, `Capacity` bindable. Handles layout switch (2→2×1, 3–4→2×2, 5–6→3×2, 7–8→4×2) and capacity-based Alpha masking. |
| `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs` | Modify. Replace bare-`Container` `playerAreasContainer` with `TournamentPlayerGrid`; add `VisibleSlotCount` bindable and `snapshottedSlots` dictionary; take snapshot in `setupGameplayInfrastructure`; use snapshot slot in `loadUserIntoPlayerArea` (remove team-side logic); clear on `teardownGameplay`. |
| `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs` | Modify. Add one `SettingsSlider<int>` labelled "Visible players" to the control panel, gated on the multiplayer-spectating branch. |
| `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs` | New. Visual test scene: colored placeholder tiles, interactive slider + add/remove buttons for eyeballing, plus automated `AddStep`/`AddAssert` coverage for each layout case and capacity transition. |

No changes to `LadderInfo`, `MultiplayerMatchIPCInfo`, `MultiplayerIPCWriter`, `IPCSnapshot`, bracket/team models, or chroma-key code.

---

## Task 1: Create `TournamentPlayerGrid` scaffold + failing test

**Files:**
- Create: `osu.Game.Tournament/Components/TournamentPlayerGrid.cs`
- Create: `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs`

- [ ] **Step 1: Write the failing test scene**

Create `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs` with this initial content:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentPlayerGrid : TournamentTestScene
    {
        private TournamentPlayerGrid grid = null!;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Clear();
            Add(grid = new TournamentPlayerGrid
            {
                RelativeSizeAxes = Axes.Both,
            });
        });

        [Test]
        public void TestDefaultCapacityIsMinimum()
        {
            AddAssert("capacity defaults to MIN_SLOTS (2)",
                () => grid.Capacity.Value == TournamentPlayerGrid.MIN_SLOTS);
            AddAssert("MIN_SLOTS is 2", () => TournamentPlayerGrid.MIN_SLOTS == 2);
            AddAssert("MAX_SLOTS is 8", () => TournamentPlayerGrid.MAX_SLOTS == 8);
        }
    }
}
```

- [ ] **Step 2: Run the test and verify it fails with compilation error**

Run:
```
dotnet build osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```
Expected: build FAILS with `CS0246: The type or namespace name 'TournamentPlayerGrid' could not be found`.

- [ ] **Step 3: Create the minimal `TournamentPlayerGrid` scaffold**

Create `osu.Game.Tournament/Components/TournamentPlayerGrid.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// A responsive grid of 2–8 tiles used by the tournament spectator overlay.
    /// Tile positions are addressed by a stable slot index; the visible subset
    /// is bounded by <see cref="Capacity"/>.
    /// </summary>
    public partial class TournamentPlayerGrid : CompositeDrawable
    {
        public const int MIN_SLOTS = 2;
        public const int MAX_SLOTS = 8;

        public BindableInt Capacity { get; } = new BindableInt(MIN_SLOTS)
        {
            MinValue = MIN_SLOTS,
            MaxValue = MAX_SLOTS,
        };

        private readonly Drawable?[] slots = new Drawable?[MAX_SLOTS];
        private readonly Container content;

        public TournamentPlayerGrid()
        {
            InternalChild = content = new Container { RelativeSizeAxes = Axes.Both };
        }

        public void Add(Drawable tile, int slotIndex)
        {
            // Implementation in Task 2.
        }

        public void Remove(int slotIndex)
        {
            // Implementation in Task 2.
        }

        public void Clear()
        {
            // Implementation in Task 2.
        }
    }
}
```

- [ ] **Step 4: Run the test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: all three assertions in `TestDefaultCapacityIsMinimum` PASS.

- [ ] **Step 5: Commit**

```
git add osu.Game.Tournament/Components/TournamentPlayerGrid.cs osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs
git commit -m "add TournamentPlayerGrid scaffold with capacity bindable

Introduces an empty TournamentPlayerGrid composite with MIN_SLOTS=2 /
MAX_SLOTS=8 constants and a Capacity bindable. Add/Remove/Clear are stubs;
behavior is implemented in follow-up commits."
```

---

## Task 2: Implement `Add` / `Remove` / `Clear` semantics

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentPlayerGrid.cs`
- Modify: `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs`

- [ ] **Step 1: Add failing tests for tile management**

Append these tests to `TestSceneTournamentPlayerGrid.cs` (inside the class body):

```csharp
[Test]
public void TestAddInsertsTileAtSlot()
{
    Drawable? tile = null;
    AddStep("add tile at slot 0", () =>
    {
        tile = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Red };
        grid.Add(tile, 0);
    });
    AddAssert("tile is a descendant of grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == grid);
}

[Test]
public void TestRemoveDisposesTileAtSlot()
{
    Drawable? tile = null;
    AddStep("add tile at slot 3", () =>
    {
        tile = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Green };
        grid.Add(tile, 3);
    });
    AddAssert("tile is in grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == grid);
    AddStep("remove slot 3", () => grid.Remove(3));
    AddAssert("tile is no longer in grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == null);
}

[Test]
public void TestClearRemovesAllTiles()
{
    Drawable? a = null;
    Drawable? b = null;
    AddStep("add two tiles", () =>
    {
        a = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Red };
        b = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Blue };
        grid.Add(a, 0);
        grid.Add(b, 1);
    });
    AddStep("clear", () => grid.Clear());
    AddAssert("tile a gone", () => a!.FindClosestParent<TournamentPlayerGrid>() == null);
    AddAssert("tile b gone", () => b!.FindClosestParent<TournamentPlayerGrid>() == null);
}
```

Also add these required usings at the top of the test file:

```csharp
using osu.Framework.Graphics.Shapes;
```

- [ ] **Step 2: Run tests and verify they fail**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: FAIL — assertions in `TestAddInsertsTileAtSlot` / `TestRemoveDisposesTileAtSlot` / `TestClearRemovesAllTiles` fail because `Add`/`Remove`/`Clear` are stubs that do nothing.

- [ ] **Step 3: Implement tile management**

Replace the stub bodies in `TournamentPlayerGrid.cs` with:

```csharp
public void Add(Drawable tile, int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
        throw new System.ArgumentOutOfRangeException(nameof(slotIndex),
            $"Slot index must be in [0, {MAX_SLOTS}).");
    if (slots[slotIndex] != null)
        throw new System.InvalidOperationException($"Slot {slotIndex} is already occupied.");

    slots[slotIndex] = tile;
    content.Add(tile);
}

public void Remove(int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
        return;
    var tile = slots[slotIndex];
    if (tile == null)
        return;

    slots[slotIndex] = null;
    content.Remove(tile, disposeImmediately: true);
}

public void Clear()
{
    for (int i = 0; i < MAX_SLOTS; i++)
        slots[i] = null;
    content.Clear(disposeChildren: true);
}
```

- [ ] **Step 4: Run tests and verify they pass**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: all four tests PASS.

- [ ] **Step 5: Commit**

```
git add osu.Game.Tournament/Components/TournamentPlayerGrid.cs osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs
git commit -m "implement TournamentPlayerGrid add/remove/clear tile management

Tiles are stored by stable slot index (0..MAX_SLOTS-1). Add rejects
out-of-range or occupied slots. Remove disposes the tile; Clear wipes
all slots. Layout sizing and capacity masking come in follow-ups."
```

---

## Task 3: Implement layout switch in `Update`

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentPlayerGrid.cs`
- Modify: `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs`

- [ ] **Step 1: Add failing tests for layout dimensions**

Append to `TestSceneTournamentPlayerGrid.cs`:

```csharp
[TestCase(2, 2, 1)]
[TestCase(3, 2, 2)]
[TestCase(4, 2, 2)]
[TestCase(5, 3, 2)]
[TestCase(6, 3, 2)]
[TestCase(7, 4, 2)]
[TestCase(8, 4, 2)]
public void TestLayoutDimensionsForVisibleCount(int tileCount, int expectedCols, int expectedRows)
{
    AddStep($"resize grid to 800x600", () => grid.Size = new osuTK.Vector2(800, 600));
    AddStep($"set capacity to {tileCount}", () => grid.Capacity.Value = tileCount);
    AddStep($"add {tileCount} tiles", () =>
    {
        for (int i = 0; i < tileCount; i++)
            grid.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Orange }, i);
    });
    AddUntilStep("tiles sized for layout",
        () =>
        {
            float expectedTileWidth = 800f / expectedCols;
            float expectedTileHeight = 600f / expectedRows;
            foreach (var child in grid.ChildrenOfType<Box>())
            {
                if (System.Math.Abs(child.DrawWidth - expectedTileWidth) > 1f) return false;
                if (System.Math.Abs(child.DrawHeight - expectedTileHeight) > 1f) return false;
            }
            return true;
        });
}
```

Required usings (add to top of file if not already present):

```csharp
using osu.Framework.Testing;
```

**Note on scene reset:** Because `[SetUp]` recreates the grid for each test, tests are independent.

**Note on tile geometry:** The tiles are added with `RelativeSizeAxes = Axes.Both` so each tile fills its parent cell. The cell containers are what the layout logic resizes; each tile's `DrawWidth`/`DrawHeight` reflects the cell it lives in.

**This requires `Add` to wrap tiles in cell containers.** Adjust `Add`:

- [ ] **Step 2: Restructure `Add` to wrap tiles in individual cell containers**

Update `TournamentPlayerGrid.cs`:

Replace the `slots` field declaration:
```csharp
private readonly Drawable?[] slots = new Drawable?[MAX_SLOTS];
```

with:
```csharp
private readonly Container?[] slotContainers = new Container?[MAX_SLOTS];
```

Replace `Add`:
```csharp
public void Add(Drawable tile, int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
        throw new System.ArgumentOutOfRangeException(nameof(slotIndex),
            $"Slot index must be in [0, {MAX_SLOTS}).");
    if (slotContainers[slotIndex] != null)
        throw new System.InvalidOperationException($"Slot {slotIndex} is already occupied.");

    var cell = new Container
    {
        Child = tile,
        Masking = true,
    };
    slotContainers[slotIndex] = cell;
    content.Add(cell);
}
```

Replace `Remove`:
```csharp
public void Remove(int slotIndex)
{
    if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
        return;
    var cell = slotContainers[slotIndex];
    if (cell == null)
        return;

    slotContainers[slotIndex] = null;
    content.Remove(cell, disposeImmediately: true);
}
```

Replace `Clear`:
```csharp
public void Clear()
{
    for (int i = 0; i < MAX_SLOTS; i++)
        slotContainers[i] = null;
    content.Clear(disposeChildren: true);
}
```

- [ ] **Step 3: Implement `Update` with layout logic**

Add this method inside `TournamentPlayerGrid`:

```csharp
protected override void Update()
{
    base.Update();

    int visibleCount = 0;
    for (int i = 0; i < MAX_SLOTS; i++)
    {
        if (slotContainers[i] != null && i < Capacity.Value)
            visibleCount++;
    }

    (int cols, int rows) = dimensionsFor(visibleCount);
    if (cols == 0 || rows == 0)
        return;

    float cellWidth = DrawWidth / cols;
    float cellHeight = DrawHeight / rows;

    int cellIndex = 0;
    for (int i = 0; i < MAX_SLOTS; i++)
    {
        var cell = slotContainers[i];
        if (cell == null)
            continue;

        if (i >= Capacity.Value)
        {
            cell.Alpha = 0;
            continue;
        }

        int col = cellIndex % cols;
        int row = cellIndex / cols;

        cell.Alpha = 1;
        cell.Size = new osuTK.Vector2(cellWidth, cellHeight);
        cell.Position = new osuTK.Vector2(col * cellWidth, row * cellHeight);

        cellIndex++;
    }
}

private static (int cols, int rows) dimensionsFor(int visibleCount)
{
    switch (visibleCount)
    {
        case 0:
            return (0, 0);
        case 1:
        case 2:
            return (2, 1);
        case 3:
        case 4:
            return (2, 2);
        case 5:
        case 6:
            return (3, 2);
        case 7:
        case 8:
        default:
            return (4, 2);
    }
}
```

Note: case 1 uses the 2×1 layout (one cell visible, one empty half) — this matches the spec's description of the `M=1, slider=2` edge case.

- [ ] **Step 4: Run tests and verify they pass**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: all `TestLayoutDimensionsForVisibleCount` cases PASS; prior tests still PASS.

- [ ] **Step 5: Commit**

```
git add osu.Game.Tournament/Components/TournamentPlayerGrid.cs osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs
git commit -m "implement TournamentPlayerGrid layout switch

Tiles are wrapped in individual cell containers. Update() sizes and
positions cells row-major based on visible count, using the 2x1 /
2x2 / 3x2 / 4x2 breakpoints. Capacity masking comes next."
```

---

## Task 4: Implement capacity-based visibility

**Files:**
- Modify: `osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs`

The layout code in Task 3 already sets `Alpha = 0` on cells with `slotIndex >= Capacity`, but it has no test coverage yet. Add explicit tests for that behavior and the reactive binding.

- [ ] **Step 1: Add failing tests for capacity masking**

Append to `TestSceneTournamentPlayerGrid.cs`:

```csharp
[Test]
public void TestTilesBeyondCapacityAreHidden()
{
    AddStep("resize grid", () => grid.Size = new osuTK.Vector2(800, 600));
    AddStep("add 4 tiles", () =>
    {
        for (int i = 0; i < 4; i++)
            grid.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Orange }, i);
    });
    AddStep("set capacity to 2", () => grid.Capacity.Value = 2);
    AddUntilStep("first two cells visible, rest hidden",
        () =>
        {
            var tileCells = grid.ChildrenOfType<Container>().Where(c => c.Masking).ToList();
            return tileCells.Count == 4 &&
                   tileCells[0].Alpha == 1 && tileCells[1].Alpha == 1 &&
                   tileCells[2].Alpha == 0 && tileCells[3].Alpha == 0;
        });
}

[Test]
public void TestCapacityIncreaseRevealsTiles()
{
    AddStep("resize grid", () => grid.Size = new osuTK.Vector2(800, 600));
    AddStep("add 4 tiles at capacity 2", () =>
    {
        grid.Capacity.Value = 2;
        for (int i = 0; i < 4; i++)
            grid.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Orange }, i);
    });
    AddStep("raise capacity to 4", () => grid.Capacity.Value = 4);
    AddUntilStep("all four cells visible",
        () =>
        {
            var tileCells = grid.ChildrenOfType<Container>().Where(c => c.Masking).ToList();
            return tileCells.Count == 4 && tileCells.All(c => c.Alpha == 1);
        });
}
```

Add the required usings (if not already imported earlier in the file):

```csharp
using System.Linq;
using osu.Framework.Graphics.Containers;
```

**Why the `Masking` filter works:** Tile cells are created in `Add` with `Masking = true` (see Task 3). The grid's internal `content` container is a default `Container` with `Masking = false`. Filtering `ChildrenOfType<Container>().Where(c => c.Masking)` returns exactly the tile cells in insertion (= slot) order.

- [ ] **Step 2: Run tests**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: both new tests PASS (the behavior was already implemented in Task 3; we're locking it in).

- [ ] **Step 3: Add interactive controls to the test scene (manual verification)**

Append an interactive `[Test]` method that exercises the slider and add/remove manually:

```csharp
[Test]
public void TestManualInteractive()
{
    for (int n = 2; n <= 8; n++)
    {
        int captured = n;
        AddStep($"fill {captured} tiles", () =>
        {
            grid.Clear();
            grid.Capacity.Value = captured;
            for (int i = 0; i < captured; i++)
            {
                grid.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Colour4.FromHSV(i / 8f, 0.6f, 0.9f),
                }, i);
            }
        });
    }
    AddStep("drag capacity down to 2", () => grid.Capacity.Value = 2);
    AddStep("drag capacity up to 8", () => grid.Capacity.Value = 8);
}
```

This doesn't need assertions — it's a hands-on visual check executed via the test runner UI.

- [ ] **Step 4: Run all grid tests, verify green**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentPlayerGrid"
```
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```
git add osu.Game.Tournament.Tests/Components/TestSceneTournamentPlayerGrid.cs
git commit -m "cover TournamentPlayerGrid capacity masking with tests

Locks in the Alpha=0 behavior for cells beyond Capacity, verifies
that raising Capacity re-reveals hidden cells, and adds a manual
interactive scene for eyeballing layout transitions."
```

---

## Task 5: Convert `TournamentGameplayDisplay` to use `TournamentPlayerGrid` with snapshot semantics

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`

This is the central change: swap the bare `Container playerAreasContainer` for `TournamentPlayerGrid`, add the `VisibleSlotCount` bindable and `snapshottedSlots` dictionary, take the snapshot in `setupGameplayInfrastructure`, rewrite `loadUserIntoPlayerArea` to use the snapshot, and clear the snapshot in `teardownGameplay`.

All edits are in one file and produced as a single commit to keep the file compiling.

- [ ] **Step 1: Change `playerAreasContainer` field type and add new members**

Open `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`. Locate the field declaration around line 67:

```csharp
private Container playerAreasContainer = null!;
```

Replace with:

```csharp
private TournamentPlayerGrid playerAreasContainer = null!;
```

Immediately below the existing `private readonly Dictionary<int, PlayerArea> playerAreas = new Dictionary<int, PlayerArea>();` declaration (around line 76), add:

```csharp
/// <summary>
/// Snapshot of <c>room.Users</c> taken when gameplay begins. Maps user ID to a
/// stable slot index in <see cref="playerAreasContainer"/>. Users that join
/// after the snapshot is taken receive no slot.
/// </summary>
private readonly Dictionary<int, int> snapshottedSlots = new Dictionary<int, int>();

/// <summary>
/// The number of player tiles the operator wants to show simultaneously.
/// Bound to <see cref="TournamentPlayerGrid.Capacity"/> in
/// <see cref="setupGameplayInfrastructure"/>. Runtime only — not persisted.
/// </summary>
public BindableInt VisibleSlotCount { get; } = new BindableInt(TournamentPlayerGrid.MIN_SLOTS)
{
    MinValue = TournamentPlayerGrid.MIN_SLOTS,
    MaxValue = TournamentPlayerGrid.MAX_SLOTS,
};
```

- [ ] **Step 2: Replace `loadUserIntoPlayerArea` body**

Locate `private void loadUserIntoPlayerArea(int userId, SpectatorGameplayState gameplayState)` (around line 254). Replace the entire method body with:

```csharp
private void loadUserIntoPlayerArea(int userId, SpectatorGameplayState gameplayState)
{
    // Ensure master clock + sync manager are set up (created once per gameplay session).
    if (masterClockContainer == null)
        setupGameplayInfrastructure(gameplayState.Beatmap);

    Debug.Assert(syncManager != null);

    // Don't create a second area for this user.
    if (playerAreas.ContainsKey(userId))
        return;

    // Only users present at snapshot time receive a slot. Users who join the
    // multiplayer room after gameplay began do not appear in the grid.
    if (!snapshottedSlots.TryGetValue(userId, out int slotIndex))
        return;

    // Create managed clock and PlayerArea on-demand so the sync manager
    // only tracks clocks that have actual scores to play.
    var playerArea = new PlayerArea(userId, syncManager.CreateManagedClock())
    {
        RelativeSizeAxes = Axes.Both,
    };

    playerAreas[userId] = playerArea;
    playerAreasContainer.Add(playerArea, slotIndex);
    playerArea.LoadScore(gameplayState.Score);

    // Bind audio adjustments from the first loaded player to keep the master clock in sync.
    if (boundAdjustments == null)
        bindAudioAdjustments(playerArea);
}
```

What was removed:
- The team-side resolution block (team ID lookup, `sideOccupied` check, early return).
- The outer anchored `Container` wrapper with `Width = 0.5f` and left/right anchors.

What was added:
- The `snapshottedSlots.TryGetValue(userId, out int slotIndex)` check.
- Direct insertion via `playerAreasContainer.Add(playerArea, slotIndex)` (the grid now wraps the tile in its own cell container).

- [ ] **Step 3: Replace `setupGameplayInfrastructure` body**

Locate `private void setupGameplayInfrastructure(WorkingBeatmap workingBeatmap)` (around line 305). Replace with:

```csharp
private void setupGameplayInfrastructure(WorkingBeatmap workingBeatmap)
{
    teardownGameplay();

    gameplayActive = true;

    // MasterGameplayClockContainer accesses the track in its constructor.
    if (!workingBeatmap.TrackLoaded)
        workingBeatmap.LoadTrack();

    playerAreasContainer = new TournamentPlayerGrid { RelativeSizeAxes = Axes.Both };
    // Bind the grid's Capacity TO the display's VisibleSlotCount (not the other way
    // around) so the operator's slider value survives a grid rebuild instead of being
    // reset to MIN_SLOTS each time gameplay restarts.
    playerAreasContainer.Capacity.BindTo(VisibleSlotCount);

    // Snapshot room users in server order (room join order), truncated to MAX_SLOTS.
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

    masterClockContainer = new MasterGameplayClockContainer(workingBeatmap, 0)
    {
        // PlayerAreas are children of the master clock container so that the master's
        // IGameplayClock is in their DI chain (matching MultiSpectatorScreen's hierarchy).
        Child = playerAreasContainer,
    };

    syncManager = new SpectatorSyncManager(masterClockContainer)
    {
        ReadyToStart = performInitialSeek,
    };

    gameplayContainer.Children = new Drawable[]
    {
        masterClockContainer,
        syncManager,
    };

    // Reset the master clock but don't start it yet —
    // performInitialSeek will seek and start once player clocks have frames.
    masterClockContainer.Reset();
}
```

The key additions relative to the original:
- `playerAreasContainer` is now a `TournamentPlayerGrid`.
- `playerAreasContainer.Capacity.BindTo(VisibleSlotCount)` keeps the slider value wired through to every freshly-built grid.
- The `snapshottedSlots` dictionary is populated from `multiplayerClient.Room.Users` in server order, truncated to `MAX_SLOTS`.

Note: `BindTo` copies the target's value into `this`. So `playerAreasContainer.Capacity.BindTo(VisibleSlotCount)` overwrites the fresh grid's default `MIN_SLOTS` with the operator's current slider value. If the operator had the slider at 6 when `LoadRequested` fired, the new grid's capacity starts at 6.

- [ ] **Step 4: Update `teardownGameplay` to clear the snapshot**

Locate `private void teardownGameplay()` (around line 340). Add `snapshottedSlots.Clear();` inside the method, immediately after `gameplayStates.Clear();`. The start of the method should now read:

```csharp
private void teardownGameplay()
{
    if (!gameplayActive)
        return;

    gameplayActive = false;
    gameplayStates.Clear();
    snapshottedSlots.Clear();

    if (syncManager != null)
    {
        foreach (var area in playerAreas.Values)
            syncManager.RemoveManagedClock(area.SpectatorPlayerClock);
    }
    // ...rest unchanged
```

Also add `playerAreasContainer.Capacity.UnbindFrom(VisibleSlotCount);` near the end of `teardownGameplay`, right before `playerAreas.Clear();` — this detaches the binding so the grid about to be replaced doesn't keep a reference back to the long-lived `VisibleSlotCount` bindable:

```csharp
    // Stop the master clock before clearing so the beatmap track doesn't keep playing.
    masterClockContainer?.Stop();

    playerAreasContainer.Capacity.UnbindFrom(VisibleSlotCount);

    playerAreas.Clear();
    gameplayContainer.Clear();
    masterClockContainer = null;
    syncManager = null;
}
```

(No null guard is needed: the `if (!gameplayActive) return;` early-return at the top of the method implies `setupGameplayInfrastructure` has already run and assigned `playerAreasContainer`.)

- [ ] **Step 5: Build the project to catch any missing usings or type errors**

Run:
```
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj
```
Expected: build succeeds with no new warnings or errors.

If the build complains about a missing type, verify the top-of-file `using` statements include:
```csharp
using osu.Framework.Bindables;
```
(This should already be imported via `using osu.Framework.Bindables;` — check and add if not.)

- [ ] **Step 6: Run the full tournament test suite**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```
Expected: all existing tests PASS (grid tests from Task 4 and any prior tests). `TestSceneGameplayScreen`'s existing `TestWarmup` / `TestStartupState` / `TestStartupStateNoCurrentMatch` should still pass because they exercise the stable-IPC (non-multiplayer) path — which doesn't construct `TournamentGameplayDisplay` at all.

- [ ] **Step 7: Commit**

```
git add osu.Game.Tournament/Components/TournamentGameplayDisplay.cs
git commit -m "route TournamentGameplayDisplay through TournamentPlayerGrid

Replaces the hard-coded 1v1 team-side layout with a unified N-tile grid.
At gameplay start, room.Users is snapshotted into a user-ID -> slot-index
map in server (join) order and truncated to MAX_SLOTS. Each user's
PlayerArea is inserted into the grid at their snapshot slot when their
Playing state arrives. VisibleSlotCount bindable (runtime only, not in
LadderInfo) is wired to the grid's Capacity each time the grid is rebuilt.

Players who leave the room mid-match keep their slot (no shift). Users
who join after the snapshot receive no slot."
```

---

## Task 6: Add "Visible players" slider to `GameplayScreen`

**Files:**
- Modify: `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`

- [ ] **Step 1: Add the slider in the multiplayer-spectating branch**

Open `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`. Locate the `if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)` block (around line 133). Currently the block runs `addMultiplayerControls(multiplayerIpc);`, constructs `gameplayDisplay`, and calls `addVolumeControls();`.

Immediately **after** the `addMultiplayerControls(multiplayerIpc);` call and **before** the `gameplayDisplay = new TournamentGameplayDisplay(...)` line is **too early** — `gameplayDisplay` has not yet been instantiated. Instead, append the slider to the control panel **after** `gameplayDisplay` has been created but **before** `addVolumeControls()`. Concretely, find this block:

```csharp
if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
{
    addMultiplayerControls(multiplayerIpc);

    // Add gameplay display as a sibling of the UI audio container
    // (not a child) so its hitsounds bypass the UI sample muting.
    gameplayDisplay = new TournamentGameplayDisplay(multiplayerIpc)
    {
        Alpha = 0,
    };

    // Position the gameplay display to match the chroma area exactly.
    chromaOuter.Add(new Container
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Height = 512,
        Child = gameplayDisplay,
    });

    multiplayerIpc.IsConnected.BindValueChanged(connected =>
    {
        ...
    }, true);

    // Add volume sliders for multiplayer spectating.
    addVolumeControls();
}
```

Insert a new `controlPanel.Add(...)` call immediately after the `chromaOuter.Add(new Container { ... });` block (i.e., after the gameplay display is positioned) and **before** the `multiplayerIpc.IsConnected.BindValueChanged(...)` call:

```csharp
chromaOuter.Add(new Container
{
    Anchor = Anchor.TopCentre,
    Origin = Anchor.TopCentre,
    Height = 512,
    Child = gameplayDisplay,
});

controlPanel.Add(new SettingsSlider<int>
{
    LabelText = "Visible players",
    Current = gameplayDisplay.VisibleSlotCount,
    KeyboardStep = 1,
});

multiplayerIpc.IsConnected.BindValueChanged(connected =>
{
    ...
}, true);
```

No new `using` statements needed — `SettingsSlider` is already imported (it's used for `Chroma width` and `Players per team` in the same method).

- [ ] **Step 2: Build**

Run:
```
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj
```
Expected: build succeeds.

- [ ] **Step 3: Run tests to confirm nothing regressed**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```
Expected: all tests PASS (no new tests added; existing green stays green).

- [ ] **Step 4: Commit**

```
git add osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs
git commit -m "add Visible players slider to tournament gameplay control panel

The slider is gated on the multiplayer-spectating path (same branch as
the Connect/Disconnect controls) and bound to the gameplay display's
VisibleSlotCount bindable. Operators can drag it down during a battle
royale to drop eliminated tiles and make surviving players larger."
```

---

## Task 7: Manual broadcast verification

**Files:** none (runtime verification).

This task is a structured walkthrough to exercise the feature end-to-end. Record notes on any issues.

- [ ] **Step 1: Launch the tournament client**

Run:
```
dotnet run --project osu.Desktop -- --tournament
```

Or launch through the IDE's tournament run configuration if present.

- [ ] **Step 2: Enable multiplayer spectating**

In the Setup screen, enable the "Use multiplayer spectating" toggle. Restart the client if prompted.

- [ ] **Step 3: Connect to a multiplayer room with 8 test accounts**

On the Gameplay screen, enter a room ID with 8 users present and click Connect. Verify the status reads "Connected (Room {id})".

- [ ] **Step 4: Start a match and verify the 8-tile layout**

Start gameplay in the multiplayer room. Within a few seconds, all 8 `PlayerArea` tiles should render in a 4×2 grid. Position of each user corresponds to their join order in the room.

- [ ] **Step 5: Drag the slider down**

Drag the "Visible players" slider from 8 to 4. Tiles at slot indices 4–7 fade out; tiles at slots 0–3 expand to fill the 2×2 layout.

- [ ] **Step 6: Drag the slider back up**

Drag from 4 to 8. The hidden tiles fade back in at their original positions; layout returns to 4×2.

- [ ] **Step 7: Simulate an elimination (user quits)**

Have one of the playing test accounts quit mid-song. Verify their tile fades to gray (existing `onPlayerQuit` behavior) and remains in its slot position. Other tiles do not shift.

- [ ] **Step 8: Verify the slider cap semantics during elimination**

With that quit player's tile still rendered (faded gray), drag the slider down one notch. If the quit player's tile is at the highest visible slot index, it disappears first. If not, some other tile fades instead (by slot index, not by elimination status — this matches spec section 3.1).

- [ ] **Step 9: Disconnect and verify cleanup**

Click Disconnect. The gameplay display fades out and the chroma areas fade in. Reconnecting and starting a new match produces a fresh grid (no leftover tiles).

- [ ] **Step 10: Record any surprises**

If any behavior diverges from the spec (`docs/superpowers/specs/2026-04-24-tournament-spectator-battle-royale-design.md` §4–§5), open a follow-up issue. Otherwise: feature is shipped.

---

## Definition of done

- [ ] All unit/scene tests under `TestSceneTournamentPlayerGrid` pass headless.
- [ ] `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj` is clean.
- [ ] Manual verification (Task 7) completed end-to-end.
- [ ] No regression in existing `TestSceneGameplayScreen` tests.
- [ ] Spec file at `docs/superpowers/specs/2026-04-24-tournament-spectator-battle-royale-design.md` remains accurate (no out-of-scope changes crept in).
