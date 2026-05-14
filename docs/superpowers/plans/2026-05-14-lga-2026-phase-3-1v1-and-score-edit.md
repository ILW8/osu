# LGA 2026 Phase 3 — 1v1 mode + match-complete + score-edit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port LGA 2025's `Use1V1Mode` (player-vs-player labelling) verbatim to the current branch, auto-flip `TournamentMatch.Completed` when a team reaches `PointsToWin`, and add operator score-edit UI (per-map slot scores + per-team set counters) to `MapPoolScreen`.

**Architecture:** Three independent slices wired through existing bindables: (1) `LadderInfo.Use1V1Mode` + 6 verbatim call-site ports re-using lazer's existing `DrawableTeamTitleWithHeader` / `DrawableTeamWithPlayers` toggle; (2) one-line addition in `GameplayScreen.updateState` cumulative branch that consults `TournamentMatch.PointsToWin`; (3) two new `ControlPanel` sub-sections in `MapPoolScreen` — per-map writes `MapScores[slot]` (slot dropdown + red/blue `SettingsNumberBox`), per-team binds `SettingsNumberBox.Current` directly to `Team{1,2}Score` (`Bindable<int?>` round-trip).

**Tech Stack:** C# 12, .NET 8, osu-framework bindables (`Bindable<T>`, `BindableInt`, `BindableWithCurrent`), `osu.Game.Overlays.Settings.SettingsDropdown` / `SettingsNumberBox`, `osu.Game.Graphics.UserInterfaceV2.LabelledSwitchButton`, NUnit 3 visual test scenes (`TournamentScreenTestScene`).

**Spec reference:** §6 of `docs/superpowers/specs/2026-05-10-lga-2026-update-design.md` (head commit `eac58b08ef`).

---

## File Structure

### Files to modify

| Path | What changes |
| --- | --- |
| `osu.Game.Tournament/Models/LadderInfo.cs` | Add `Use1V1Mode` bindable (default false). |
| `osu.Game.Tournament/Screens/TournamentMatchScreen.cs` | Subscribe to `LadderInfo.Use1V1Mode.BindValueChanged` and trigger `CurrentMatch.TriggerChange()` so dependent screens refresh on toggle. |
| `osu.Game.Tournament/Components/DrawableTeamHeader.cs` | Resolve `LadderInfo`; in `LoadComplete`, bind `Use1V1Mode` to swap `Team Red` ⇄ `Red player` (uppercase). |
| `osu.Game.Tournament/Screens/Ladder/Components/DrawableMatchTeam.cs` | In `load`, bind `Use1V1Mode` to resize from 180×40 → 260×40 in 1v1 mode. |
| `osu.Game.Tournament/Screens/TeamIntro/SeedingScreen.cs` | `LeftInfo` ctor gains `bool use1V1Mode`; toggle swaps "Average Rank:" → "Rank:" and skips the per-player row foreach. Caller passes `LadderInfo.Use1V1Mode.Value`. |
| `osu.Game.Tournament/Screens/TeamIntro/TeamIntroScreen.cs` | `CurrentMatchChanged` picks `DrawableTeamTitleWithHeader` vs `DrawableTeamWithPlayers` per side based on `LadderInfo.Use1V1Mode`. |
| `osu.Game.Tournament/Screens/TeamWin/TeamWinScreen.cs` | `update()` picks `DrawableTeamTitleWithHeader` vs `DrawableTeamWithPlayers` for the winner based on `LadderInfo.Use1V1Mode`. |
| `osu.Game.Tournament/Screens/Setup/SetupScreen.cs` | Add `LabelledSwitchButton` row "1v1 mode" bound to `LadderInfo.Use1V1Mode` (after "Use multiplayer spectating", before `restartButton`). |
| `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs` | In `updateState`'s cumulative-scoring branch, after the existing `Team{1,2}Score.Value++`, flip `Completed.Value = true` when either set count `>= PointsToWin`. |
| `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs` | Add two `ControlPanel` sub-sections (per-map score editor + per-team set-count editor) after the existing "Reset" button. Subscribe to `CurrentMatch.Round.ValueChanged` for dropdown-item refresh on round changes within a match. |

### Test files

| Path | Purpose |
| --- | --- |
| `osu.Game.Tournament.Tests/Screens/TestSceneSetupScreen.cs` | Add `TestUse1V1Toggle` — clicks the new switch, asserts `Ladder.Use1V1Mode.Value` flips. |
| `osu.Game.Tournament.Tests/Screens/TestSceneTeamIntroScreen.cs` | Add `TestUse1V1Display` — toggle bindable, assert `DrawableTeamTitleWithHeader` is present when true / `DrawableTeamWithPlayers` when false. |
| `osu.Game.Tournament.Tests/Screens/TestSceneGameplayScreen.cs` | Add `TestMatchAutoComplete` — pump cumulative sets until red wins 3, assert `match.Completed.Value == true`; reset and stop at 2, assert false. |
| `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs` | Add `TestScoreEditApply` (per-map writes `MapScores[slot]`) and `TestScoreEditTeamSetCounters` (per-team textbox edits `Team{1,2}Score`). |

### Files NOT touched (out of scope)

- `TournamentMatch.cs` — `PointsToWin` already correct (line 102); `Team{1,2}Score` already `Bindable<int?>`.
- `DrawableTeamTitleWithHeader.cs` — already exists on this branch (`osu.Game.Tournament/Components/DrawableTeamTitleWithHeader.cs`).
- Test infrastructure (`TournamentScreenTestScene`) — `Ladder` and `IPCInfo` cached fixtures already cover Phase 3's needs.

---

## Task 1: Add `LadderInfo.Use1V1Mode` bindable

**Files:**
- Modify: `osu.Game.Tournament/Models/LadderInfo.cs:54`

- [ ] **Step 1: Add the bindable**

Open `osu.Game.Tournament/Models/LadderInfo.cs`. After `Bindable<bool> UseMultiplayerSpectating` (around line 54), insert:

```csharp
/// <summary>
/// When <c>true</c>, text elements referring to "Team"s are updated to "Player"s and
/// team players lists are hidden. Setup-screen toggle. Default off so legacy bracket.json
/// round-trips unchanged.
/// </summary>
public Bindable<bool> Use1V1Mode = new Bindable<bool>(false);
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/Models/LadderInfo.cs
git commit -m "add LadderInfo.Use1V1Mode bindable for 1v1 mode toggle"
```

---

## Task 2: Setup screen — `1v1 mode` toggle row

**Files:**
- Modify: `osu.Game.Tournament/Screens/Setup/SetupScreen.cs:111-117` (after "Use multiplayer spectating", before `restartButton`)
- Test: `osu.Game.Tournament.Tests/Screens/TestSceneSetupScreen.cs`

- [ ] **Step 1: Write the failing test**

Replace the entire body of `osu.Game.Tournament.Tests/Screens/TestSceneSetupScreen.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Tournament.Screens.Setup;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneSetupScreen : TournamentScreenTestScene
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new SetupScreen());
        }

        [Test]
        public void TestUse1V1Toggle()
        {
            AddStep("ensure off", () => Ladder.Use1V1Mode.Value = false);

            AddStep("click 1v1 switch", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(l => l.Label == "1v1 mode");
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("Use1V1Mode is true", () => Ladder.Use1V1Mode.Value, () => Is.True);

            AddStep("click again", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(l => l.Label == "1v1 mode");
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("Use1V1Mode is false", () => Ladder.Use1V1Mode.Value, () => Is.False);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneSetupScreen.TestUse1V1Toggle"`
Expected: FAIL — `First` throws because no `LabelledSwitchButton` has `Label == "1v1 mode"`.

- [ ] **Step 3: Add the toggle row**

In `osu.Game.Tournament/Screens/Setup/SetupScreen.cs`, inside `reload()` at line 109, insert a new entry into the `children` list immediately after the existing "Use multiplayer spectating" row (line 111-116). Change:

```csharp
            var children = new List<Drawable>
            {
                new LabelledSwitchButton
                {
                    Label = "Use multiplayer spectating",
                    Description = "When enabled, the overlay connects to a multiplayer room for match data instead of reading from the stable client's IPC files.",
                    Current = LadderInfo.UseMultiplayerSpectating,
                },
                restartButton,
            };
```

To:

```csharp
            var children = new List<Drawable>
            {
                new LabelledSwitchButton
                {
                    Label = "Use multiplayer spectating",
                    Description = "When enabled, the overlay connects to a multiplayer room for match data instead of reading from the stable client's IPC files.",
                    Current = LadderInfo.UseMultiplayerSpectating,
                },
                new LabelledSwitchButton
                {
                    Label = "1v1 mode",
                    Description = "Text elements referring to \"Team\"s will be updated to \"Player\"s and team players lists will be hidden.",
                    Current = LadderInfo.Use1V1Mode,
                },
                restartButton,
            };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneSetupScreen.TestUse1V1Toggle"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Screens/Setup/SetupScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneSetupScreen.cs
git commit -m "add 1v1 mode toggle to SetupScreen with passing test"
```

---

## Task 3: `TournamentMatchScreen` — trigger refresh on `Use1V1Mode` change

**Files:**
- Modify: `osu.Game.Tournament/Screens/TournamentMatchScreen.cs:14-20`

Background: TeamIntroScreen, TeamWinScreen, SeedingScreen all derive from `TournamentMatchScreen` and rebuild their UI in `CurrentMatchChanged`. Toggling `Use1V1Mode` must force them to re-evaluate. This mirrors the LGA 2025 tag verbatim.

- [ ] **Step 1: Add the binding in `LoadComplete`**

Edit `osu.Game.Tournament/Screens/TournamentMatchScreen.cs`. In `LoadComplete()` (line 14), after the existing two-line `CurrentMatch.BindTo(...)` / `CurrentMatch.BindValueChanged(...)` block, append:

```csharp
            LadderInfo.Use1V1Mode.BindValueChanged(_ => CurrentMatch.TriggerChange());
```

So the final method body is:

```csharp
        protected override void LoadComplete()
        {
            base.LoadComplete();

            CurrentMatch.BindTo(LadderInfo.CurrentMatch);
            CurrentMatch.BindValueChanged(CurrentMatchChanged, true);

            LadderInfo.Use1V1Mode.BindValueChanged(_ => CurrentMatch.TriggerChange());
        }
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/Screens/TournamentMatchScreen.cs
git commit -m "trigger CurrentMatch refresh when Use1V1Mode toggles"
```

---

## Task 4: `DrawableTeamHeader` — swap "Team Red" / "Red player" labels

**Files:**
- Modify: `osu.Game.Tournament/Components/DrawableTeamHeader.cs`

- [ ] **Step 1: Replace the file body**

Open `osu.Game.Tournament/Components/DrawableTeamHeader.cs` and replace its full content with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class DrawableTeamHeader : TournamentSpriteTextWithBackground
    {
        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private readonly TeamColour colour;

        public DrawableTeamHeader(TeamColour colour)
        {
            this.colour = colour;
            Background.Colour = TournamentGame.GetTeamColour(colour);

            Text.Colour = TournamentGame.TEXT_COLOUR;
            Text.Scale = new Vector2(0.6f);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ladder.Use1V1Mode.BindValueChanged(use1V1 => Text.Text = use1V1.NewValue
                                                                         ? $"{colour} player".ToUpperInvariant()
                                                                         : $"Team {colour}".ToUpperInvariant(),
                true);
        }
    }
}
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/Components/DrawableTeamHeader.cs
git commit -m "swap DrawableTeamHeader text between Team {colour} and {colour} player based on Use1V1Mode"
```

---

## Task 5: `DrawableMatchTeam` — widen tile in 1v1 mode

**Files:**
- Modify: `osu.Game.Tournament/Screens/Ladder/Components/DrawableMatchTeam.cs:80-128` (in the `load` method)

Background: LGA 2025 tag widens the ladder match tile from 150×40 (default) to 260×40 (1v1) to give a single-player name room to breathe. The constructor today sets `Size = new Vector2(150, 40)` (line 64). Port LGA's resize block verbatim.

- [ ] **Step 1: Add the binding inside `load`**

In `osu.Game.Tournament/Screens/Ladder/Components/DrawableMatchTeam.cs`, locate the `load` method (line 80-137). Right after the `colourWinner = …` initialisation (line 87) and before the `InternalChildren = …` assignment (line 89), insert the `Use1V1Mode` size binding from the LGA tag. Also make `ladderInfo` accessible inside `load`. The full modified `load` body becomes:

```csharp
        [BackgroundDependencyLoader(true)]
        private void load(LadderEditorScreen ladderEditor)
        {
            this.ladderEditor = ladderEditor;

            colourWinner = losers
                ? Color4Extensions.FromHex("#8E7F48")
                : Color4Extensions.FromHex("#1462AA");

            if (ladderInfo != null)
            {
                ladderInfo.Use1V1Mode.BindValueChanged(use1V1 =>
                {
                    Size = new Vector2(use1V1.NewValue ? 260 : 180, 40);
                }, true);
            }

            InternalChildren = new Drawable[]
            {
                // ... unchanged ...
```

Leave the rest of `load` untouched. Note that the constructor's `Size = new Vector2(150, 40)` (line 64) becomes effectively dead code (the binding fires immediately on `LoadComplete`, replacing it with 180×40 or 260×40 — matching the LGA tag's "180" default). Do NOT remove the constructor line — the LGA tag kept it as the pre-LoadComplete sizing.

- [ ] **Step 2: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/Screens/Ladder/Components/DrawableMatchTeam.cs
git commit -m "widen DrawableMatchTeam tile in 1v1 mode (180->260)"
```

---

## Task 6: `SeedingScreen.LeftInfo` — 1v1 constructor flag

**Files:**
- Modify: `osu.Game.Tournament/Screens/TeamIntro/SeedingScreen.cs:112` (caller) and `osu.Game.Tournament/Screens/TeamIntro/SeedingScreen.cs:255-285` (LeftInfo ctor)

- [ ] **Step 1: Update the caller**

Change line 112 from:

```csharp
                new LeftInfo(currentTeam.Value) { Position = new Vector2(55, 150), },
```

To:

```csharp
                new LeftInfo(currentTeam.Value, LadderInfo.Use1V1Mode.Value) { Position = new Vector2(55, 150), },
```

- [ ] **Step 2: Extend the `LeftInfo` constructor**

In the same file, locate `LeftInfo` (line 255). Change the constructor signature and body. Replace the existing `LeftInfo` constructor body (lines 257-285) with:

```csharp
            public LeftInfo(TournamentTeam? team, bool use1V1Mode)
            {
                FillFlowContainer fill;

                Width = 200;

                if (team == null) return;

                InternalChildren = new Drawable[]
                {
                    fill = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new TeamDisplay(team) { Margin = new MarginPadding { Bottom = 30 } },
                            new RowDisplay(use1V1Mode ? "Rank:" : "Average Rank:", $"#{team.AverageRank:#,0}"),
                            new RowDisplay("Seed:", team.Seed.Value),
                            new RowDisplay("Last year's placing:", team.LastYearPlacing.Value),
                            new Container { Margin = new MarginPadding { Bottom = 30 } },
                        }
                    },
                };

                if (use1V1Mode)
                    return;

                foreach (var p in team.Players)
                    fill.Add(new RowDisplay(p.Username, p.Rank?.ToString("\\##,0") ?? "-"));
            }
```

(Differences from current: ctor gains `bool use1V1Mode`; "Average Rank:" → "Rank:" when 1v1; early-return before the `foreach (var p in team.Players)` so per-player rows are skipped in 1v1.)

- [ ] **Step 3: Add the `Use1V1Mode` refresh subscription**

Still in `SeedingScreen.cs`, locate the line `currentTeam.BindValueChanged(teamChanged, true);` (currently line 71). Append immediately after it:

```csharp
            LadderInfo.Use1V1Mode.BindValueChanged(_ => updateTeamDisplay());
```

- [ ] **Step 4: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Screens/TeamIntro/SeedingScreen.cs
git commit -m "SeedingScreen LeftInfo gains 1v1 flag — Rank label and skip players list"
```

---

## Task 7: `TeamIntroScreen` — swap title-with-header / with-players

**Files:**
- Modify: `osu.Game.Tournament/Screens/TeamIntro/TeamIntroScreen.cs:37-73`
- Test: `osu.Game.Tournament.Tests/Screens/TestSceneTeamIntroScreen.cs`

- [ ] **Step 1: Write the failing test**

Replace the body of `osu.Game.Tournament.Tests/Screens/TestSceneTeamIntroScreen.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.TeamIntro;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneTeamIntroScreen : TournamentScreenTestScene
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Ladder.CurrentMatch.Value = new TournamentMatch
            {
                Team1 = { Value = Ladder.Teams.FirstOrDefault(t => t.Acronym.Value == "USA") },
                Team2 = { Value = Ladder.Teams.FirstOrDefault(t => t.Acronym.Value == "JPN") },
                Round = { Value = Ladder.Rounds.FirstOrDefault(g => g.Name.Value == "Finals") }
            };

            Add(new TeamIntroScreen
            {
                FillMode = FillMode.Fit,
                FillAspectRatio = 16 / 9f
            });
        }

        [Test]
        public void TestUse1V1Display()
        {
            AddStep("disable 1v1", () => Ladder.Use1V1Mode.Value = false);
            AddAssert("renders DrawableTeamWithPlayers", () =>
                this.ChildrenOfType<DrawableTeamWithPlayers>().Count(), () => Is.EqualTo(2));
            AddAssert("no DrawableTeamTitleWithHeader", () =>
                this.ChildrenOfType<DrawableTeamTitleWithHeader>().Count(), () => Is.EqualTo(0));

            AddStep("enable 1v1", () => Ladder.Use1V1Mode.Value = true);
            AddAssert("renders DrawableTeamTitleWithHeader", () =>
                this.ChildrenOfType<DrawableTeamTitleWithHeader>().Count(), () => Is.EqualTo(2));
            AddAssert("no DrawableTeamWithPlayers", () =>
                this.ChildrenOfType<DrawableTeamWithPlayers>().Count(), () => Is.EqualTo(0));
        }
    }
}
```

Note: the original test scene constructed a separate `[Cached] LadderInfo`. We must rely on `TournamentScreenTestScene.Ladder` (the cached scene-base `LadderInfo`) instead, otherwise the new toggle binding fires against a different instance and `TournamentMatchScreen.LoadComplete` won't see the trigger. The existing test fixture was wired this way before set-cumulative changes landed; this update aligns it with the active pattern.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTeamIntroScreen.TestUse1V1Display"`
Expected: FAIL — the second "renders DrawableTeamTitleWithHeader" assertion fails because the screen still renders `DrawableTeamWithPlayers` unconditionally.

- [ ] **Step 3: Update `TeamIntroScreen.CurrentMatchChanged`**

In `osu.Game.Tournament/Screens/TeamIntro/TeamIntroScreen.cs`, add a `[Resolved]` field at the top of the class (before `mainContainer`):

```csharp
        [Resolved]
        private LadderInfo ladderInfo { get; set; } = null!;
```

Add the `using` if missing: `using osu.Game.Tournament.Models;` is already present.

Then replace the entire `CurrentMatchChanged` method body (lines 37-73) with:

```csharp
        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);

            mainContainer.Clear();

            if (match.NewValue == null)
                return;

            const float y_flag_offset = 292;

            const float y_offset = 460;

            Drawable team1Display = ladderInfo.Use1V1Mode.Value
                                        ? new DrawableTeamTitleWithHeader(match.NewValue.Team1.Value, TeamColour.Red)
                                        : new DrawableTeamWithPlayers(match.NewValue.Team1.Value, TeamColour.Red);
            Drawable team2Display = ladderInfo.Use1V1Mode.Value
                                        ? new DrawableTeamTitleWithHeader(match.NewValue.Team2.Value, TeamColour.Blue)
                                        : new DrawableTeamWithPlayers(match.NewValue.Team2.Value, TeamColour.Blue);

            team1Display.Position = new Vector2(165, y_offset);
            team2Display.Position = new Vector2(740, y_offset);

            mainContainer.Children = new[]
            {
                new RoundDisplay(match.NewValue)
                {
                    Position = new Vector2(100, 100)
                },
                new DrawableTeamFlag(match.NewValue.Team1.Value)
                {
                    Position = new Vector2(165, y_flag_offset),
                },
                team1Display,
                new DrawableTeamFlag(match.NewValue.Team2.Value)
                {
                    Position = new Vector2(740, y_flag_offset),
                },
                team2Display,
            };
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTeamIntroScreen.TestUse1V1Display"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Screens/TeamIntro/TeamIntroScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneTeamIntroScreen.cs
git commit -m "swap TeamIntroScreen team displays for DrawableTeamTitleWithHeader in 1v1 mode"
```

---

## Task 8: `TeamWinScreen` — swap title-with-header / with-players

**Files:**
- Modify: `osu.Game.Tournament/Screens/TeamWin/TeamWinScreen.cs:67-123` (in `update()`)

- [ ] **Step 1: Swap the winner display**

`TeamWinScreen` derives from `TournamentScreen`, which already exposes `protected LadderInfo LadderInfo { get; }` — use that inherited property directly, do NOT add a shadowing `[Resolved] private LadderInfo ladderInfo` field.

In the `update()` Scheduler.AddOnce delegate (line 67-123), change the `mainContainer.Children = …` block. Specifically, the existing `new DrawableTeamWithPlayers(match.Winner, match.WinnerColour)` (line 117) becomes a switched local:

Replace the `mainContainer.Children = new Drawable[]` block (lines 89-120) with:

```csharp
            Drawable teamDisplay = LadderInfo.Use1V1Mode.Value
                                       ? new DrawableTeamTitleWithHeader(match.Winner, match.WinnerColour)
                                       : new DrawableTeamWithPlayers(match.Winner, match.WinnerColour);

            mainContainer.Children = new Drawable[]
            {
                new DrawableTeamFlag(match.Winner)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Position = new Vector2(-300, 10),
                    Scale = new Vector2(2f)
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = 260,
                    Children = new Drawable[]
                    {
                        new RoundDisplay(match)
                        {
                            Margin = new MarginPadding { Bottom = 30 },
                        },
                        new TournamentSpriteText
                        {
                            Text = "WINNER",
                            Font = OsuFont.Torus.With(size: 100, weight: FontWeight.Bold),
                            Margin = new MarginPadding { Bottom = 50 },
                        },
                        teamDisplay,
                    }
                },
            };
            mainContainer.FadeOut();
            mainContainer.Delay(2000).FadeIn(1600, Easing.OutQuint);
```

- [ ] **Step 2: Verify compile**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: build succeeds.

- [ ] **Step 3: Run tournament test suite**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: all tests pass except the pre-existing `TestProtectIconRender` (which needs a logged-in osu API and fails locally — see project memory `project_lga2025_to_lga2026.md`).

- [ ] **Step 4: Commit**

```bash
git add osu.Game.Tournament/Screens/TeamWin/TeamWinScreen.cs
git commit -m "swap TeamWinScreen winner display for DrawableTeamTitleWithHeader in 1v1 mode"
```

---

## Task 9: `GameplayScreen` — match-complete auto-detect

**Files:**
- Modify: `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs:316-328` (cumulative-scoring `setComplete` block)
- Test: `osu.Game.Tournament.Tests/Screens/TestSceneGameplayScreen.cs`

Spec ref: §6.2. Wraps `match.Completed.Value = true` inside the existing cumulative branch so warmup auto-bypass is already covered (the cumulative branch already early-returns when `warmup.Value` is true, line 294).

- [ ] **Step 1: Write the failing test**

Append the following test methods to `osu.Game.Tournament.Tests/Screens/TestSceneGameplayScreen.cs`, after the existing `TestScoreCumulativeDelta` test (around line 150):

```csharp
        [Test]
        public void TestMatchAutoCompleteAtPointsToWin()
        {
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);
            AddStep("set BestOf 5 (PointsToWin = 3)", () => Ladder.CurrentMatch.Value!.Round.Value!.BestOf.Value = 5);
            AddStep("reset completion", () => Ladder.CurrentMatch.Value!.Completed.Value = false);
            AddStep("zero scores", () =>
            {
                Ladder.CurrentMatch.Value!.Team1Score.Value = 0;
                Ladder.CurrentMatch.Value!.Team2Score.Value = 0;
            });

            createScreen();
            toggleWarmup();

            AddStep("add 1 set (maps 1 & 2)", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 1 }, Map2Id = { Value = 2 } }));
            playSet(mapIds: new[] { 1, 2 }, redWins: true);
            AddAssert("not complete after set 1", () => Ladder.CurrentMatch.Value!.Completed.Value, () => Is.False);

            AddStep("add set 2 (maps 3 & 4)", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 3 }, Map2Id = { Value = 4 } }));
            playSet(mapIds: new[] { 3, 4 }, redWins: true);
            AddAssert("not complete after set 2", () => Ladder.CurrentMatch.Value!.Completed.Value, () => Is.False);

            AddStep("add set 3 (maps 5 & 6)", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 5 }, Map2Id = { Value = 6 } }));
            playSet(mapIds: new[] { 5, 6 }, redWins: true);
            AddAssert("team1 set wins is 3", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(3));
            AddAssert("Completed is true", () => Ladder.CurrentMatch.Value!.Completed.Value, () => Is.True);
        }

        private void playSet(int[] mapIds, bool redWins)
        {
            foreach (int mapId in mapIds)
            {
                int captured = mapId;
                AddStep($"switch to map {captured}", () => IPCInfo.Beatmap.Value = new TournamentBeatmap { OnlineID = captured });
                AddStep("set state: idle", () => IPCInfo.State.Value = TourneyState.Idle);
                AddStep("set state: playing", () => IPCInfo.State.Value = TourneyState.Playing);
                AddStep("add score", () =>
                {
                    IPCInfo.Score1.Value = redWins ? 1_000_000 : 0;
                    IPCInfo.Score2.Value = redWins ? 0 : 1_000_000;
                });
                AddStep("set state: ranking", () => IPCInfo.State.Value = TourneyState.Ranking);
                AddWaitStep("wait a bit", 4);
                AddStep("clear scores", () =>
                {
                    IPCInfo.Score1.Value = 0;
                    IPCInfo.Score2.Value = 0;
                });
            }
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneGameplayScreen.TestMatchAutoCompleteAtPointsToWin"`
Expected: FAIL on the final `Completed is true` assert — team1 reaches 3 set wins but `Completed` stays false.

- [ ] **Step 3: Add the auto-complete flip**

In `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`, locate the cumulative-scoring `setComplete` block (lines 316-328). Replace:

```csharp
                                    if (setComplete)
                                    {
                                        var scores = currentSet.GetSetScores(CurrentMatch.Value);

                                        if (scores != null)
                                        {
                                            if (scores.Item1 > scores.Item2)
                                                CurrentMatch.Value.Team1Score.Value++;
                                            else
                                                CurrentMatch.Value.Team2Score.Value++;
                                        }
                                    }
```

With:

```csharp
                                    if (setComplete)
                                    {
                                        var scores = currentSet.GetSetScores(CurrentMatch.Value);

                                        if (scores != null)
                                        {
                                            if (scores.Item1 > scores.Item2)
                                                CurrentMatch.Value.Team1Score.Value++;
                                            else
                                                CurrentMatch.Value.Team2Score.Value++;

                                            // LGA 2026 §3.6 first-to-PointsToWin (3 for BestOf 5). Nullable
                                            // comparison: null >= 3 is false, so unstarted matches can't auto-complete.
                                            int pointsToWin = CurrentMatch.Value.PointsToWin;
                                            if (CurrentMatch.Value.Team1Score.Value >= pointsToWin
                                                || CurrentMatch.Value.Team2Score.Value >= pointsToWin)
                                            {
                                                CurrentMatch.Value.Completed.Value = true;
                                            }
                                        }
                                    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneGameplayScreen.TestMatchAutoCompleteAtPointsToWin"`
Expected: PASS.

- [ ] **Step 5: Re-run prior cumulative tests to ensure no regression**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneGameplayScreen"`
Expected: all `TestSceneGameplayScreen` tests pass (`TestWarmup`, `TestScoreAddCumulative`, `TestScoreAddCumulativeTiebreaker`, `TestScoreCumulativeDelta`, `TestStartupState`, `TestStartupStateNoCurrentMatch`, plus the new `TestMatchAutoCompleteAtPointsToWin`).

- [ ] **Step 6: Commit**

```bash
git add osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneGameplayScreen.cs
git commit -m "auto-flip TournamentMatch.Completed when set wins reach PointsToWin"
```

---

## Task 10: `MapPoolScreen` — per-map score-edit UI

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:143-202` (control panel), `:507-512` (CurrentMatchChanged)
- Test: `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs`

Spec ref: §6.3. This task adds **only** the per-map block (slot dropdown + red/blue + Apply). Task 11 adds the per-team set-count block.

- [ ] **Step 1: Write the failing test**

Append to `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs`, after the existing tests:

```csharp
        [Test]
        public void TestScoreEditApply()
        {
            AddStep("seed beatmaps with slot names", () =>
            {
                var round = Ladder.CurrentMatch.Value!.Round.Value!;
                round.Beatmaps.Clear();
                round.Beatmaps.Add(new RoundBeatmap { ID = 101, Beatmap = new TournamentBeatmap { OnlineID = 101 }, SlotName = "NM1", Mods = "NM" });
                round.Beatmaps.Add(new RoundBeatmap { ID = 102, Beatmap = new TournamentBeatmap { OnlineID = 102 }, SlotName = "NM2", Mods = "NM" });

                Ladder.CurrentMatch.TriggerChange();
            });

            AddStep("type slot NM1, red 100, blue 50, apply", () =>
            {
                var dropdown = screen.ChildrenOfType<SettingsDropdown<string?>>().First();
                dropdown.Current.Value = "NM1";

                var numberBoxes = screen.ChildrenOfType<SettingsNumberBox>().ToList();
                // First two number boxes are red / blue score (per-team set counters added in Task 11 follow).
                numberBoxes[0].Current.Value = 100;
                numberBoxes[1].Current.Value = 50;

                var applyButton = screen.ChildrenOfType<TourneyButton>().First(b => b.Text == "Apply map score");
                applyButton.TriggerClick();
            });

            AddAssert("MapScores NM1 = (100, 50)", () =>
            {
                var ms = Ladder.CurrentMatch.Value!.MapScores;
                return ms.TryGetValue("NM1", out var t) && t.Item1 == 100 && t.Item2 == 50;
            });
        }
```

Also add these `using` directives to the top of `TestSceneMapPoolScreen.cs` if not already present:

```csharp
using osu.Game.Overlays.Settings;
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestScoreEditApply"`
Expected: FAIL — `First()` on the `SettingsDropdown<string?>` collection throws because no such control exists yet.

- [ ] **Step 3: Add the per-map score-edit block to `MapPoolScreen`**

At the top of `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs`, add the using if missing:

```csharp
using osu.Game.Overlays.Settings;
```

In the `MapPoolScreen` class, add three private fields next to the existing button declarations (around line 43, after `buttonBlueProtect`):

```csharp
        private SettingsDropdown<string?> mapScoreEditDropdown = null!;
        private SettingsNumberBox redScoreTextBox = null!;
        private SettingsNumberBox blueScoreTextBox = null!;
```

In the `ControlPanel.Children` array (line 145-200), insert the per-map score-edit block right before the closing `}` of the `Children = new Drawable[] { … }` block (after the existing `OsuCheckbox` for "Split display by mods", line 195-199). The block to append:

```csharp
                        new ControlPanel.Spacer(),
                        new TournamentSpriteText
                        {
                            Text = "Edit map scores",
                        },
                        mapScoreEditDropdown = new SettingsDropdown<string?>
                        {
                            LabelText = "Slot",
                            Items = Array.Empty<string?>(),
                        },
                        redScoreTextBox = new SettingsNumberBox
                        {
                            LabelText = "Red score",
                        },
                        blueScoreTextBox = new SettingsNumberBox
                        {
                            LabelText = "Blue score",
                        },
                        new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Apply map score",
                            Action = applyMapScoreEdit,
                        },
```

Add the `applyMapScoreEdit` method to `MapPoolScreen` (place it after `reset()`, before `updateSetsDisplay()`):

```csharp
        private void applyMapScoreEdit()
        {
            if (CurrentMatch.Value == null) return;
            if (mapScoreEditDropdown.Current.Value is not string slot) return;
            if (redScoreTextBox.Current.Value is not int red) return;
            if (blueScoreTextBox.Current.Value is not int blue) return;

            // MapScores values are Tuple<long, long>; int → long widening is implicit and lossless.
            CurrentMatch.Value.MapScores[slot] = new Tuple<long, long>(red, blue);
        }
```

Update `CurrentMatchChanged` (line 507) to refresh the dropdown items when the current match (or its round) changes. Replace the existing method:

```csharp
        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);
            updateDisplay();
            updateSetsDisplay();
        }
```

With:

```csharp
        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);
            updateDisplay();
            updateSetsDisplay();

            // Spec §6.3: re-bind on both CurrentMatch changes AND round-within-match changes
            // so the slot dropdown follows a ref editing the round.
            match.OldValue?.Round.ValueChanged -= onRoundBindableChanged;
            if (match.NewValue != null)
                match.NewValue.Round.ValueChanged += onRoundBindableChanged;

            refreshSlotItems();
        }

        private void onRoundBindableChanged(ValueChangedEvent<TournamentRound?> _) => refreshSlotItems();

        private void refreshSlotItems()
        {
            var round = CurrentMatch.Value?.Round.Value;
            mapScoreEditDropdown.Items = round?.Beatmaps.Select(b => b.SlotName).Cast<string?>().ToArray() ?? Array.Empty<string?>();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestScoreEditApply"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs
git commit -m "add per-map score editor to MapPoolScreen control panel"
```

---

## Task 11: `MapPoolScreen` — per-team set-count editor

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs` (extend the control panel from Task 10 and the `CurrentMatchChanged` re-bind)
- Test: `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs`

Spec ref: §6.3 (per-team sub-section). Refs need this because `Team{1,2}Score` is only auto-incremented by `GameplayScreen`; if a ref fixes a per-map score after the fact, the set count won't move automatically.

- [ ] **Step 1: Write the failing test**

Append to `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs`:

```csharp
        [Test]
        public void TestScoreEditTeamSetCounters()
        {
            AddStep("zero set counts", () =>
            {
                Ladder.CurrentMatch.Value!.Team1Score.Value = 0;
                Ladder.CurrentMatch.Value!.Team2Score.Value = 0;
            });

            AddStep("type red 2, blue 1 into set-count boxes", () =>
            {
                var numberBoxes = screen.ChildrenOfType<SettingsNumberBox>().ToList();
                // Order in the control panel (from Tasks 10 + 11): red-map, blue-map, red-set, blue-set.
                numberBoxes[2].Current.Value = 2;
                numberBoxes[3].Current.Value = 1;
            });

            AddAssert("Team1Score is 2", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(2));
            AddAssert("Team2Score is 1", () => Ladder.CurrentMatch.Value!.Team2Score.Value, () => Is.EqualTo(1));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestScoreEditTeamSetCounters"`
Expected: FAIL — `numberBoxes[2]` is out of range (only red+blue map-score boxes exist).

- [ ] **Step 3: Add the per-team block**

In `MapPoolScreen.cs`, add two new fields next to the others:

```csharp
        private SettingsNumberBox team1SetScoreTextBox = null!;
        private SettingsNumberBox team2SetScoreTextBox = null!;
```

In the `ControlPanel.Children` array, append the per-team block immediately after the "Apply map score" button added in Task 10:

```csharp
                        new ControlPanel.Spacer(),
                        new TournamentSpriteText
                        {
                            Text = "Edit set scores",
                        },
                        team1SetScoreTextBox = new SettingsNumberBox
                        {
                            LabelText = "Red set score",
                        },
                        team2SetScoreTextBox = new SettingsNumberBox
                        {
                            LabelText = "Blue set score",
                        },
```

In `CurrentMatchChanged`, after the `refreshSlotItems()` call added in Task 10, re-bind the two number-box `Current` properties to the new match's score bindables:

```csharp
        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);
            updateDisplay();
            updateSetsDisplay();

            match.OldValue?.Round.ValueChanged -= onRoundBindableChanged;
            if (match.NewValue != null)
                match.NewValue.Round.ValueChanged += onRoundBindableChanged;

            refreshSlotItems();

            if (match.NewValue != null)
            {
                team1SetScoreTextBox.Current = match.NewValue.Team1Score;
                team2SetScoreTextBox.Current = match.NewValue.Team2Score;
            }
        }
```

`SettingsNumberBox.Current` is `Bindable<int?>` — same type as `Team1Score`/`Team2Score` — so the `IHasCurrentValue<int?>.Current` setter routes through `BindableWithCurrent` and the textbox value tracks the match field both ways.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestScoreEditTeamSetCounters"`
Expected: PASS.

- [ ] **Step 5: Re-run prior MapPool tests to ensure no regression**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen"`
Expected: all `TestSceneMapPoolScreen` tests pass except the pre-existing `TestProtectIconRender` (logged-in-API-required, unchanged).

- [ ] **Step 6: Commit**

```bash
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs
git commit -m "add per-team set-count editor textboxes to MapPoolScreen"
```

---

## Task 12: Full tournament-test suite verification + plan checkbox tick

**Files:**
- Modify: `docs/superpowers/plans/2026-05-14-lga-2026-phase-3-1v1-and-score-edit.md` (this file — tick boxes)

- [ ] **Step 1: Run the full tournament test suite**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`
Expected: 144/145 (or similar) tests pass. The single failure should still be the pre-existing `TestProtectIconRender` (logged-in osu API required — see `project_lga2025_to_lga2026.md`). If any other test fails, diagnose before proceeding.

- [ ] **Step 2: Update the LGA project memory**

In `C:\Users\daohe\.claude\projects\C--Users-daohe-RiderProjects-osu\memory\project_lga2025_to_lga2026.md`, update the phase status table — change Phase 3's `Plan` cell from "Not written" to `docs/superpowers/plans/2026-05-14-lga-2026-phase-3-1v1-and-score-edit.md` and its `Code` cell to "Done — commits {RANGE}".

Add a "Phase 3 — DONE" subsection in the same style as the existing Phase 2 subsection, summarising: `Use1V1Mode` bindable + 6 call-site ports, `GameplayScreen` auto-complete at `PointsToWin`, `MapPoolScreen` two-section score-edit UI (per-map + per-team), `boundRound.Round.ValueChanged` re-bind for slot dropdown.

- [ ] **Step 3: Tick all `- [ ]` checkboxes in this plan file**

Use Edit/replace to swap every `- [ ]` to `- [x]` in `docs/superpowers/plans/2026-05-14-lga-2026-phase-3-1v1-and-score-edit.md`. Per `[[feedback-plan-progression]]`: keep plan checkbox state in sync with commit reality.

- [ ] **Step 4: Final commit**

```bash
git add docs/superpowers/plans/2026-05-14-lga-2026-phase-3-1v1-and-score-edit.md
git commit -m "plans: tick Phase 3 task checkboxes to match committed implementation"
```

---

## Notes

**Scope honesty:** the spec listed "5 call sites" for the `Use1V1Mode` port (§6.1) but the LGA 2025 tag actually touches 6 files (the spec missed `DrawableTeamHeader.cs`, which swaps the "Team Red" / "Red player" header text). The plan ports all 6 — losing the header swap would leave the in-gameplay header reading "TEAM RED" even in 1v1 mode, defeating the purpose. Tasks 4 (header) and 5 (match-team tile) are the two not enumerated in the spec table.

**Why no test for Tasks 3, 4, 5, 6, 8 individually:** Task 3 is a one-line wiring; Tasks 4–6 are layout-only ports with no behaviour assertable beyond rendering (covered visually by `TestSceneTeamIntroScreen.TestUse1V1Display` in Task 7); Task 8 is verified indirectly by Task 7's display assertions (`TeamWinScreen` rendering covered by manual visual run). Adding more unit tests here would over-constrain layout choices and slow iteration. If a future regression shows up, add a targeted test then.

**Custom-styling caveat (§6.3 widget mismatch):** `SettingsDropdown<T>` / `SettingsNumberBox` are sized for the full-width settings overlay (label-above-input). They will look chunkier than the surrounding `TourneyButton` / `OsuCheckbox` rows in the `ControlPanel`. This is acceptable for an operator-only panel — function over polish. If the broadcast-graphic styling diverges enough to be visible to viewers, revisit in a follow-up (not Phase 3).

**Build limitation:** `dotnet build osu.sln` fails on this machine (missing iOS/Android workloads). Use project-level builds (`osu.Game.Tournament.csproj` / `osu.Game.Tournament.Tests.csproj`) per the project memory.

**Subagent dispatch note:** per `[[feedback-subagent-model]]`, pass `model: "opus"` when dispatching code-writing subagents for these tasks.
