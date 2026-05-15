# Tournament multiplayer room controls in the left navigation panel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `MultiplayerRoomConnectionControls` out of `GameplayScreen`'s right `ControlPanel` and into `TournamentSceneManager`'s always-visible left navigation column so the controls are reachable from every tournament screen.

**Architecture:** Inject `MatchIPCInfo` into `TournamentSceneManager` via BDL. Wrap the left column's existing nav `FillFlowContainer` in an `OsuScrollContainer` (unconditional — keeps layout code simple and prevents nav-button clipping). When the resolved IPC is `MultiplayerMatchIPCInfo`, append a `Separator` + `MultiplayerRoomConnectionControls` to the end of the nav flow. Delete the gameplay-screen copy of the controls (and its preceding `ControlPanel.Spacer`). `SetupScreen` is intentionally left untouched and keeps its in-content copy.

**Tech Stack:** C# 12, .NET 8, osu-framework BDL dependency injection, `osu.Game.Graphics.Containers.OsuScrollContainer`, NUnit 3 visual test scenes (`TournamentTestScene` / `TestScene` from osu-framework).

**Spec reference:** `docs/superpowers/specs/2026-05-15-tournament-multiplayer-controls-left-panel-design.md`.

---

## File Structure

### Files to modify

| Path | What changes |
| --- | --- |
| `osu.Game.Tournament/TournamentSceneManager.cs` | Add `[BackgroundDependencyLoader] private void load(MatchIPCInfo ipc)` parameter. Wrap the existing left-column `buttons` flow in an `OsuScrollContainer` and switch the flow to `RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y`. After the existing nav children, if `ipc is MultiplayerMatchIPCInfo multiplayerIpc`, append `new Separator()` + `new MultiplayerRoomConnectionControls(multiplayerIpc)`. |
| `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs` | Delete `addMultiplayerControls` method (lines 189–196) and the `addMultiplayerControls(multiplayerIpc);` call (line 135). Other multiplayer-specific behavior in that block (`gameplayDisplay` creation, "Visible players" slider, chroma hiding, `IsConnected` fade binding, `addVolumeControls()`) is preserved. |
| `osu.Game.Tournament.Tests/TournamentTestScene.cs` | Replace the `[Cached] protected MatchIPCInfo IPCInfo = new MatchIPCInfo();` initializer with a virtual factory: BDL `load()` calls `CreateIPCInfo()`, assigns to `IPCInfo`, and `Dependencies.CacheAs<MatchIPCInfo>(IPCInfo)`. Adds `protected virtual MatchIPCInfo CreateIPCInfo() => new MatchIPCInfo();`. No behavior change for existing subclasses. |

### Files to create

| Path | Purpose |
| --- | --- |
| `osu.Game.Tournament.Tests/TestSceneTournamentSceneManagerMultiplayer.cs` | Sibling of the existing `TestSceneTournamentSceneManager`. Overrides `CreateIPCInfo` to return a `MultiplayerMatchIPCInfo`, additionally caches it as the concrete `MultiplayerMatchIPCInfo` type in BDL so any future direct `[Resolved] MultiplayerMatchIPCInfo` works (matches production behaviour from `TournamentGameBase`). Contains one `[Test]` asserting the left column hosts a `MultiplayerRoomConnectionControls`. |

### Files NOT touched (intentional, per spec)

- `osu.Game.Tournament/Components/MultiplayerRoomConnectionControls.cs` — reused verbatim in the new location (already `RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y` with a centred "Multiplayer Room" header).
- `osu.Game.Tournament/Screens/Setup/SetupScreen.cs` — keeps its in-content copy; users will see the controls twice while on SetupScreen. Spec calls this out as intentional.
- `osu.Game.Tournament/TournamentGameBase.cs` — IPC caching is already correct (both `MatchIPCInfo` and `MultiplayerMatchIPCInfo` are cached when multiplayer mode is on).
- `osu.Game.Tournament.Tests/TestSceneTournamentSceneManager.cs` — unchanged; continues to cover the file-based code path.

---

## Task 1: Refactor `TournamentTestScene` to allow IPC override

The new test scene needs to inject a `MultiplayerMatchIPCInfo` where `TournamentTestScene` currently force-caches a base `MatchIPCInfo`. Re-caching the same type throws (`DependencyContainer` rejects duplicate registrations), so we replace the field-level `[Cached]` with a virtual factory + BDL-side `CacheAs`. No subclass behavior change because the default factory returns the same `new MatchIPCInfo()` instance.

**Files:**
- Modify: `osu.Game.Tournament.Tests/TournamentTestScene.cs:28-52`

- [x] **Step 1: Replace the cached field with a private-set property + virtual factory**

Open `osu.Game.Tournament.Tests/TournamentTestScene.cs`. Replace the existing `IPCInfo` declaration (line 28–29):

```csharp
        [Cached]
        protected MatchIPCInfo IPCInfo { get; private set; } = new MatchIPCInfo();
```

with:

```csharp
        protected MatchIPCInfo IPCInfo { get; private set; } = null!;

        /// <summary>
        /// Subclasses can override to inject a derived IPC type (e.g.
        /// <see cref="osu.Game.Tournament.IPC.MultiplayerMatchIPCInfo"/>). The returned
        /// instance is cached as <see cref="MatchIPCInfo"/> in the dependency container.
        /// </summary>
        protected virtual MatchIPCInfo CreateIPCInfo() => new MatchIPCInfo();
```

- [x] **Step 2: Cache the IPC instance from the BDL `load`**

In the same file, replace the existing BDL `load` (lines 36–52):

```csharp
        [BackgroundDependencyLoader]
        private void load(TournamentStorage storage)
        {
            Ladder.Ruleset.Value ??= rulesetStore.AvailableRulesets.First();

            match = CreateSampleMatch();

            Ladder.Rounds.Add(match.Round.Value!);
            Ladder.Matches.Add(match);
            Ladder.Teams.Add(match.Team1.Value!);
            Ladder.Teams.Add(match.Team2.Value!);

            Ruleset.BindTo(Ladder.Ruleset);
            Dependencies.CacheAs(new StableInfo(storage));

            Add(DialogOverlay);
        }
```

with:

```csharp
        [BackgroundDependencyLoader]
        private void load(TournamentStorage storage)
        {
            Ladder.Ruleset.Value ??= rulesetStore.AvailableRulesets.First();

            IPCInfo = CreateIPCInfo();
            Dependencies.CacheAs(IPCInfo);

            match = CreateSampleMatch();

            Ladder.Rounds.Add(match.Round.Value!);
            Ladder.Matches.Add(match);
            Ladder.Teams.Add(match.Team1.Value!);
            Ladder.Teams.Add(match.Team2.Value!);

            Ruleset.BindTo(Ladder.Ruleset);
            Dependencies.CacheAs(new StableInfo(storage));

            Add(DialogOverlay);
        }
```

- [x] **Step 3: Verify build**

Run: `dotnet build osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: build succeeds.

- [x] **Step 4: Run all Tournament tests to confirm no regression**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: all existing tests pass. The refactor is behaviour-preserving for current subclasses (default factory returns the same `new MatchIPCInfo()`).

- [x] **Step 5: Commit**

```bash
git add osu.Game.Tournament.Tests/TournamentTestScene.cs
git commit -m "tests: factor out IPC info construction in TournamentTestScene"
```

---

## Task 2: Add failing `TestSceneTournamentSceneManagerMultiplayer`

We add the new test scene before changing `TournamentSceneManager` so the test fails first (RED), then the production change in Task 3 turns it green. The assertion has to be scoped to the **left column** specifically: today both `SetupScreen` and `GameplayScreen` render their own `MultiplayerRoomConnectionControls` (Setup at `SetupScreen.cs:127`, Gameplay at `GameplayScreen.cs:194`), so a tree-wide count would already be 2 before Task 3 and would mask the regression. We count only controls whose ancestor chain does **not** pass through any `TournamentScreen` — those are the left-column instances. Pre-Task-3 the count is 0 (RED); post-Task-3 it's 1 (GREEN); Task 4 doesn't affect the count because it only removes the gameplay-screen copy.

**Files:**
- Create: `osu.Game.Tournament.Tests/TestSceneTournamentSceneManagerMultiplayer.cs`

- [x] **Step 1: Create the test scene file**

Write `osu.Game.Tournament.Tests/TestSceneTournamentSceneManagerMultiplayer.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Screens;

namespace osu.Game.Tournament.Tests
{
    public partial class TestSceneTournamentSceneManagerMultiplayer : TournamentTestScene
    {
        protected override MatchIPCInfo CreateIPCInfo() => new MultiplayerMatchIPCInfo();

        [BackgroundDependencyLoader]
        private void load()
        {
            // Mirror TournamentGameBase's production caching so any future
            // [Resolved] MultiplayerMatchIPCInfo consumer also resolves correctly.
            Dependencies.CacheAs((MultiplayerMatchIPCInfo)IPCInfo);

            Add(new TournamentSceneManager());
        }

        [Test]
        public void TestLeftColumnHostsMultiplayerControls()
        {
            // Left-column controls are the ones whose ancestor chain does NOT pass through
            // any TournamentScreen. SetupScreen and GameplayScreen also render their own
            // copies today; scoping by "not inside a screen" isolates the left-column count
            // and keeps this assertion stable across Task 4 (which removes the gameplay copy).
            AddAssert("left column hosts exactly one MultiplayerRoomConnectionControls",
                () => this.ChildrenOfType<MultiplayerRoomConnectionControls>()
                          .Count(c => !isInsideAnyTournamentScreen(c)),
                () => Is.EqualTo(1));
        }

        private static bool isInsideAnyTournamentScreen(Drawable drawable)
        {
            for (Drawable? p = drawable.Parent; p != null; p = p.Parent)
            {
                if (p is TournamentScreen)
                    return true;
            }

            return false;
        }
    }
}
```

- [x] **Step 2: Verify build**

Run: `dotnet build osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: build succeeds.

- [x] **Step 3: Run the new test to confirm it fails (RED)**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentSceneManagerMultiplayer"`
Expected: `TestLeftColumnHostsMultiplayerControls` FAILS with the assertion reporting **0** instead of the expected 1 (left column doesn't host the controls yet).

- [x] **Step 4: Commit the red state**

```bash
git add osu.Game.Tournament.Tests/TestSceneTournamentSceneManagerMultiplayer.cs
git commit -m "tests: add failing scene for multiplayer controls in left panel"
```

---

## Task 3: Inject `MatchIPCInfo` + add scroll wrapper + conditional multiplayer controls to `TournamentSceneManager`

Single commit because the scroll wrapper and the multiplayer-conditional injection both touch the left-column construction in the same BDL block; splitting them would force a transient state where the multiplayer controls render but cannot fit (no scroll). Combined, the change is one focused unit.

**Files:**
- Modify: `osu.Game.Tournament/TournamentSceneManager.cs:60-163`

- [x] **Step 1: Add the new `using` directives**

Open `osu.Game.Tournament/TournamentSceneManager.cs`. After the existing `using osu.Game.Graphics;` line (line 13), insert:

```csharp
using osu.Game.Graphics.Containers;
using osu.Game.Tournament.IPC;
```

(`osu.Game.Graphics.Containers` provides `OsuScrollContainer`; `osu.Game.Tournament.IPC` provides `MatchIPCInfo` / `MultiplayerMatchIPCInfo`. `Drawable`, `Anchor`, `FillFlowContainer`, etc. are already imported.)

- [x] **Step 2: Add the `MatchIPCInfo` BDL parameter**

In the same file, change the BDL signature (line 59–60):

```csharp
        [BackgroundDependencyLoader]
        private void load()
```

to:

```csharp
        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
```

- [x] **Step 3: Replace the left-column container with a scroll-wrapped flow + conditional multiplayer controls**

In the same file, replace the entire left-column `Container` block (currently lines 115–156, the second top-level `InternalChildren` entry):

```csharp
                new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = CONTROL_AREA_WIDTH,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            Colour = Color4.Black,
                            RelativeSizeAxes = Axes.Both,
                        },
                        buttons = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(5),
                            Padding = new MarginPadding(5),
                            Children = new Drawable[]
                            {
                                new ScreenButton(typeof(SetupScreen)) { Text = "Setup", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(TeamEditorScreen)) { Text = "Team Editor", RequestSelection = SetScreen },
                                new ScreenButton(typeof(RoundEditorScreen)) { Text = "Rounds Editor", RequestSelection = SetScreen },
                                new ScreenButton(typeof(LadderEditorScreen)) { Text = "Bracket Editor", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(ScheduleScreen), Key.S) { Text = "Schedule", RequestSelection = SetScreen },
                                new ScreenButton(typeof(LadderScreen), Key.B) { Text = "Bracket", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(TeamIntroScreen), Key.I) { Text = "Team Intro", RequestSelection = SetScreen },
                                new ScreenButton(typeof(SeedingScreen), Key.D) { Text = "Seeding", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(MapPoolScreen), Key.M) { Text = "Map Pool", RequestSelection = SetScreen },
                                new ScreenButton(typeof(GameplayScreen), Key.G) { Text = "Gameplay", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(TeamWinScreen), Key.W) { Text = "Win", RequestSelection = SetScreen },
                                new Separator(),
                                new ScreenButton(typeof(DrawingsScreen)) { Text = "Drawings", RequestSelection = SetScreen },
                                new ScreenButton(typeof(ShowcaseScreen)) { Text = "Showcase", RequestSelection = SetScreen },
                            }
                        },
                    },
                },
```

with:

```csharp
                new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = CONTROL_AREA_WIDTH,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            Colour = Color4.Black,
                            RelativeSizeAxes = Axes.Both,
                        },
                        // Scroll wrapper is unconditional so nav buttons + (optional) multiplayer
                        // controls never clip when the combined auto-size exceeds the column height.
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = buttons = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(5),
                                Padding = new MarginPadding(5),
                                Children = new Drawable[]
                                {
                                    new ScreenButton(typeof(SetupScreen)) { Text = "Setup", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(TeamEditorScreen)) { Text = "Team Editor", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(RoundEditorScreen)) { Text = "Rounds Editor", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(LadderEditorScreen)) { Text = "Bracket Editor", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(ScheduleScreen), Key.S) { Text = "Schedule", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(LadderScreen), Key.B) { Text = "Bracket", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(TeamIntroScreen), Key.I) { Text = "Team Intro", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(SeedingScreen), Key.D) { Text = "Seeding", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(MapPoolScreen), Key.M) { Text = "Map Pool", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(GameplayScreen), Key.G) { Text = "Gameplay", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(TeamWinScreen), Key.W) { Text = "Win", RequestSelection = SetScreen },
                                    new Separator(),
                                    new ScreenButton(typeof(DrawingsScreen)) { Text = "Drawings", RequestSelection = SetScreen },
                                    new ScreenButton(typeof(ShowcaseScreen)) { Text = "Showcase", RequestSelection = SetScreen },
                                },
                            },
                        },
                    },
                },
```

Note the changes vs. the original:
- New `OsuScrollContainer` wraps `buttons`.
- `buttons.RelativeSizeAxes` changed from `Axes.Both` → `Axes.X`; `AutoSizeAxes = Axes.Y` added so the flow grows with its content (required for the scroll container to compute scroll extent).
- Children are unchanged from the existing set.

- [x] **Step 4: Append multiplayer controls when the IPC is `MultiplayerMatchIPCInfo`**

In the same file, immediately after the closing `};` of the top-level `InternalChildren = new Drawable[] { ... };` assignment (currently line 157, just before the `foreach (var drawable in screens) drawable.Hide();` loop on line 159), insert:

```csharp
            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
            {
                buttons.AddRange(new Drawable[]
                {
                    new Separator(),
                    new MultiplayerRoomConnectionControls(multiplayerIpc),
                });
            }
```

The result is that the BDL `load(MatchIPCInfo ipc)` body now ends with:

```csharp
            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
            {
                buttons.AddRange(new Drawable[]
                {
                    new Separator(),
                    new MultiplayerRoomConnectionControls(multiplayerIpc),
                });
            }

            foreach (var drawable in screens)
                drawable.Hide();

            SetScreen(typeof(SetupScreen));
        }
```

(`Separator` is the existing private nested class in `TournamentSceneManager` — 20 px tall, full width — already used as inter-group separators in the nav list, matching the spec.)

- [x] **Step 5: Build and run the failing test — expect it to pass now (GREEN)**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentSceneManagerMultiplayer"`
Expected: `TestLeftColumnHostsMultiplayerControls` PASSES (the left column now hosts exactly 1 `MultiplayerRoomConnectionControls`).

- [x] **Step 6: Run the original file-based test scene to confirm no regression**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentSceneManager$"`
Expected: PASSES. The file-based path renders the left column unchanged in shape (scroll wrapper present but the auto-sized flow fits, so no visual difference).

- [x] **Step 7: Run the full Tournament test suite**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: all tests pass.

- [x] **Step 8: Commit**

```bash
git add osu.Game.Tournament/TournamentSceneManager.cs
git commit -m "move multiplayer room controls to left navigation column"
```

---

## Task 4: Remove the now-duplicated controls from `GameplayScreen`

The left column now hosts the controls on every screen, so the gameplay-screen copy is redundant. Removing it also deletes the trailing `ControlPanel.Spacer` that preceded the controls in the right `ControlPanel` — the volume header's own `ControlPanel.Spacer` (inside `addVolumeControls`) still provides the visual gap before the Volume section.

**Files:**
- Modify: `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs:132-135,189-196`

- [x] **Step 1: Delete the `addMultiplayerControls` call**

Open `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`. Locate the multiplayer block in `load` (lines 132–173). Remove only the two lines that add the controls (line 135 + its leading comment on line 132):

Before:

```csharp
            // Add multiplayer room connection controls if using multiplayer spectating.
            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
            {
                addMultiplayerControls(multiplayerIpc);

                // Add gameplay display as a sibling of the UI audio container
```

After:

```csharp
            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
            {
                // Add gameplay display as a sibling of the UI audio container
```

(The `// Add multiplayer room connection controls if using multiplayer spectating.` comment and the `addMultiplayerControls(multiplayerIpc);` line, plus the now-orphaned blank line, are removed. The opening brace of the `if` block stays on its own line.)

- [x] **Step 2: Delete the `addMultiplayerControls` method**

In the same file, delete the entire `addMultiplayerControls` method (currently lines 189–196):

```csharp
        private void addMultiplayerControls(MultiplayerMatchIPCInfo multiplayerIpc)
        {
            controlPanel.AddRange(new Drawable[]
            {
                new ControlPanel.Spacer(),
                new MultiplayerRoomConnectionControls(multiplayerIpc),
            });
        }
```

- [x] **Step 3: Build**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds. (`MultiplayerRoomConnectionControls` is still used by the moved-to-`TournamentSceneManager` and by `SetupScreen`, so the import / type stays referenced.)

- [x] **Step 4: Run `TestSceneGameplayScreen` to confirm no regression**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneGameplayScreen"`
Expected: all `TestSceneGameplayScreen` tests pass. The file-based code path is unaffected; the multiplayer path no longer has the room controls in the right panel but retains the gameplay display, "Visible players" slider, chroma hiding, `IsConnected` fade, and the Volume section.

- [x] **Step 5: Run the new multiplayer scene-manager test again**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentSceneManagerMultiplayer"`
Expected: PASSES. The assertion counts only left-column instances (those not inside any `TournamentScreen`), so removing the gameplay-screen copy leaves the count at 1.

- [x] **Step 6: Run the full Tournament test suite**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: all tests pass.

- [x] **Step 7: Commit**

```bash
git add osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs
git commit -m "remove multiplayer room controls from gameplay right panel"
```

---

## Task 5: Manual verification

Run through the spec's manual verification steps in order. Pause on any failure and capture the divergence before fixing.

**Prerequisites:**
- Build succeeds on the latest commit from Task 4.
- A test tournament directory exists with a `bracket.json`. If none, the file-based path will still launch and just show empty data — that is enough for steps 1 and 2.

- [ ] **Step 1: Run the tournament client in file-based mode (multiplayer spectating disabled)**

Run: launch `osu.Game.Tournament` (e.g. from the `osu! (Tournament)` configuration in JetBrains Rider, or `dotnet run --project osu.Desktop -- --tournament`).

Expected: left navigation column looks identical to today — nav buttons + separators, no multiplayer section. No visual regressions. The scroll wrapper is invisible because the nav fits within the column height at standard resolutions.

- [ ] **Step 2: Enable multiplayer spectating + restart**

On `SetupScreen`, toggle "Use multiplayer spectating" on, click Save changes (the "Save and restart to apply" button appears), confirm the client restarts.

Expected: the left column now contains the nav buttons, a trailing `Separator`, and the "Multiplayer Room" controls beneath. The controls render the bold "Multiplayer Room" header, Room ID + Password textboxes, Connect/Reconnect buttons, and the gray "Disconnected" status.

- [ ] **Step 3: Verify the controls persist across every screen**

Click through Setup → Schedule → Bracket → Map Pool → Gameplay → Win in turn.

Expected: the Multiplayer Room controls remain in the left column on every screen and remain interactive (room ID textbox accepts focus, Connect button accepts clicks).

- [ ] **Step 4: Verify the right `ControlPanel` on `GameplayScreen`**

Navigate to GameplayScreen. Inspect the right-side control panel.

Expected: contents are Warmup toggle, Show chat toggle, Chroma width slider, Players per team slider, Visible players slider, Volume header + Master / Music / Effects sliders. **No** "Multiplayer Room" header and **no** Room ID / Password textboxes — those moved to the left.

- [ ] **Step 5: Connect to a room from a non-gameplay screen**

While on `ScheduleScreen` (i.e. not `GameplayScreen`), enter a known-good room ID in the left-column textbox and press Connect (or Enter while focused).

Expected: status text turns "Connected (Room <id>)" in light green. Switching to `GameplayScreen` shows the player grid fade in normally (existing `IsConnected` binding in `GameplayScreen` still wires `gameplayDisplay.FadeIn`).

- [ ] **Step 6: Trigger an invite from a non-gameplay screen**

While on `ScheduleScreen`, have a second account invite the tournament client to a room.

Expected: the invite text (orange) and Accept / Dismiss buttons appear in the left-column controls. Accept connects the client to the new room; Dismiss clears the invite. (No change in behaviour vs. the previous gameplay-screen placement — the controls are the same component.)

- [ ] **Step 7: Verify scroll behaviour by shrinking the window vertically**

Resize the tournament window until the combined nav + multiplayer controls flow exceeds the column height (around 600 px tall typically suffices).

Expected: the left column scrolls vertically; both the lowest nav entry ("Showcase") and the multiplayer controls remain reachable via mouse scroll wheel inside the column.

- [ ] **Step 8: Capture verification result**

If all 7 steps above pass, the feature is shipped. If any step fails, do **not** proceed to a PR — re-open the relevant task and fix in a follow-up commit (TDD: add or extend a test first if the failure is something that could be caught automatically).

---

## Self-review notes

Final consistency check against the spec (`docs/superpowers/specs/2026-05-15-tournament-multiplayer-controls-left-panel-design.md`):

- ✅ `TournamentSceneManager` layout change — Task 3 wraps `buttons` in `OsuScrollContainer`, switches to `RelativeSizeAxes.X + AutoSizeAxes.Y`.
- ✅ Conditional injection of `MultiplayerRoomConnectionControls` — Task 3 step 4 (`if (ipc is MultiplayerMatchIPCInfo multiplayerIpc) { ... }`).
- ✅ `GameplayScreen` removal of `addMultiplayerControls` method + call site — Task 4 steps 1 and 2.
- ✅ `SetupScreen` left untouched — explicitly listed in "Files NOT touched".
- ✅ `MultiplayerRoomConnectionControls` reused unchanged — also listed in "Files NOT touched".
- ✅ New `TestSceneTournamentSceneManagerMultiplayer` — Task 2; existing `TestSceneTournamentSceneManager` unchanged (continues to cover file-based path).
- ✅ Scroll-wrapping behaviour change in file-based case acknowledged — Task 3 step 3 + verification step 7.
- ✅ Single `Separator` between nav and controls — Task 3 step 4 uses the existing private `Separator` class (20 px, full width).

No placeholders / TBDs remain. Types and method names used in later tasks (`MatchIPCInfo`, `MultiplayerMatchIPCInfo`, `MultiplayerRoomConnectionControls`, `Separator`, `OsuScrollContainer`, `CreateIPCInfo`) match what's defined / referenced in earlier tasks.
