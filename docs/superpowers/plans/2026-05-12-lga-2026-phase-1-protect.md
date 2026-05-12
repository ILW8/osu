# LGA 2026 Phase 1 — Pick/ban order + Protect + ProtectIcon implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the LGA 2026 protect mechanic and interleaved Ban/Protect/Pick draft order to the tournament overlay's MapPool screen, including the data model (`ChoiceType.Protect`, `TournamentMatch.Protects`, two new `TournamentRound` bindables), a new corner-badge `TournamentProtectIcon` component, the `TournamentBeatmapPanel` rework that hosts it, MapPool screen control-panel buttons and click-handling rules, and round-editor UI to configure protects per round.

**Architecture:** Adopts the data shape and panel layout from upstream PR ppy/osu#36200 (which keeps `Protects` as a collection distinct from `PicksBans` so right-click removal can prefer pick-over-protect). Order logic is then overridden in `MapPoolScreen.setNextMode()` with hardcoded LGA arrays — this branch is LGA-only by design, so `BanCount` / `ProtectCount` become inert at draft time but stay on the model for `bracket.json` round-trip. `TournamentGame.GetTeamColour` narrows from `ColourInfo` to `Color4` to match PR #36200 (all existing callers continue to compile via implicit `Color4 → ColourInfo` conversion at use sites).

**Tech Stack:** C# / osu-framework drawable hierarchy, Newtonsoft.Json for `bracket.json` round-trip, NUnit + osu-framework test-scene pattern (`TournamentScreenTestScene` / `TournamentTestScene`). Build: `dotnet build osu.sln`. Test assembly: `osu.Game.Tournament.Tests`.

**Spec reference:** `docs/superpowers/specs/2026-05-10-lga-2026-update-design.md` §4.1–§4.10. This plan covers Phase 1 only. Phases 2–4 will get their own plans.

> **Note (added 2026-05-12 after Phase 1 implementation):** Phase 4 (MapPool 65/35 split layout, spec §7) has been upgraded to a hard requirement for the bracket-stage broadcast and must ship before weekend 1 (deadline: 2026-05-15). It is the next plan to be written and executed; it touches only `MapPoolScreen.cs` layout code and is functionally orthogonal to Phase 1 (no rework of Phase 1's protect mechanic). See spec §8.3 / §8.4.

**Scope notes:**

- The existing `TestPickBanOrder` / `TestBanOrderMultipleBans` / `TestMultipleTeamBans` test the count-based `setNextMode` that is being replaced; they become redundant with the new `TestProtectBanPickOrder` (which exercises the full 16-click LGA sequence). Per spec §4.10 ("Update or delete"), this plan deletes them and adds `TestProtectBanPickOrder` as the canonical click→mode-advance coverage. Spec §8.2's "rewrite rather than delete" prose is the rationale for keeping such coverage *somewhere*; `TestProtectBanPickOrder` is where.
- `TournamentMatch.Reset()` currently clears only `PicksBans`. Spec §4.1 calls out clearing `Protects` (new), `Sets`, and `MapScores` while we're touching the method. The `Sets` / `MapScores` part is a latent bug the spec authorizes fixing in passing.
- `TournamentGame.GetTeamColour` narrows to `Color4`. All 4 callers on this branch (`TournamentBeatmapPanel`, `TournamentSetPanel`, `DrawableTeamHeader`, plus the new `TournamentProtectIcon`) assign the return into a `ColourInfo`-typed property (`BorderColour` / `Background.Colour`); `Color4 → ColourInfo` is an implicit conversion, so no caller changes are needed.

**File structure:**

| File | Responsibility |
| --- | --- |
| `osu.Game.Tournament/Models/BeatmapChoice.cs` | Modify. Add `Protect = 2` to `ChoiceType`. |
| `osu.Game.Tournament/Models/TournamentMatch.cs` | Modify. Add `Protects` collection; update `Reset()` to also clear `Protects`, `Sets`, `MapScores`. |
| `osu.Game.Tournament/Models/TournamentRound.cs` | Modify. Add `ProtectCount : BindableInt` and `AllowPickingOpponentProtects : BindableBool`. |
| `osu.Game.Tournament/TournamentGame.cs` | Modify. Narrow `GetTeamColour` return type from `ColourInfo` to `Color4`. |
| `osu.Game.Tournament/Components/TournamentProtectIcon.cs` | New. Corner-badge `Container`: 45°-rotated `Box` wedge + `ShieldAlt` `SpriteIcon`; `TeamColour?` setter controls tint + alpha. |
| `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs` | Modify. Introduce `borderBox` wrapper; host `protectIcon` outside it; relocate `modIcon`; subscribe to both `PicksBans` and `Protects`; route flash/dim/border mutations to `borderBox`. |
| `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs` | Modify. Add `Red Protect` / `Blue Protect` buttons + `setMode` colour wiring; replace `setNextMode` with hardcoded LGA order arrays; rewrite `addForBeatmap` for protect awareness; rewrite right-click branch in `OnMouseDown` for two-stage removal; update `reset` to clear both collections. |
| `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs` | Modify. Reflow `RoundRow` to 0.24f columns; add `# of Protects` slider and `Allow picking opponent's protects` checkbox; float `Add beatmap` button to its own row. |
| `osu.Game.Tournament.Tests/Components/TestSceneTournamentBeatmapPanel.cs` | Modify. Add `TestProtectIconRender` covering Red/Blue protect rendering and the flash-doesn't-affect-protect-icon invariant. |
| `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs` | Modify. Delete `TestPickBanOrder` / `TestBanOrderMultipleBans` / `TestMultipleTeamBans`; add `TestProtectBanPickOrder`, `TestDisallowPickOpponentProtect`, `TestRemoveProtect`. |
| `osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs` | Modify. Add `TestProtectFields` covering the new bindables round-trip through the round-editor UI. |

No changes to `LadderInfo`, IPC code (`MultiplayerMatchIPCInfo`, `MultiplayerIPCWriter`, `IPCSnapshot`), gameplay screens, `TournamentSetPanel`, or bracket/team models.

---

## Task 1: Add `ChoiceType.Protect` enum value

**Files:**
- Modify: `osu.Game.Tournament/Models/BeatmapChoice.cs:33-37`

- [x] **Step 1: Add `Protect` to the enum**

Edit `osu.Game.Tournament/Models/BeatmapChoice.cs`. Replace the `ChoiceType` enum body so the existing numeric ordering of `Pick = 0` / `Ban = 1` is preserved and `Protect = 2` is appended:

```csharp
[JsonConverter(typeof(StringEnumConverter))]
public enum ChoiceType
{
    Pick,    // 0 — preserved
    Ban,     // 1 — preserved
    Protect, // 2 — new (LGA 2026)
}
```

The `[JsonConverter(typeof(StringEnumConverter))]` already on the enum means new bracket files serialize `"Protect"` as a string; older bracket files only contain `"Pick"` / `"Ban"` and deserialize unchanged. New files with `"Protect"` cannot be opened by older binaries — acceptable since this branch is LGA-only.

- [x] **Step 2: Build to confirm no callers broke**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED. (No `switch (newChoice.Type)` exhaustiveness warnings — C# treats missing enum cases as a warning only when there's an explicit `default` requirement, which there isn't here. The `switch` in `TournamentBeatmapPanel.updateState` doesn't list a default; we'll add handling for `Protect` in Task 6 once `borderBox` exists.)

- [x] **Step 3: Commit**

```
git add osu.Game.Tournament/Models/BeatmapChoice.cs
git commit -m "add ChoiceType.Protect enum value

Appended as numeric value 2 so existing bracket.json files with
Pick=0 / Ban=1 deserialize unchanged. Wires up the LGA 2026 protect
mechanic per spec §4.1."
```

---

## Task 2: Add `TournamentMatch.Protects` and update `Reset()`

**Files:**
- Modify: `osu.Game.Tournament/Models/TournamentMatch.cs:52-54`, `:126-133`

- [x] **Step 1: Add the `Protects` collection**

Edit `osu.Game.Tournament/Models/TournamentMatch.cs`. After the existing `PicksBans` declaration (currently line 52), insert a new `Protects` collection so the two stay adjacent in the class:

Find:
```csharp
public readonly ObservableCollection<BeatmapChoice> PicksBans = new ObservableCollection<BeatmapChoice>();

public readonly ObservableCollection<MatchSet> Sets = new ObservableCollection<MatchSet>();
```

Replace with:
```csharp
public readonly ObservableCollection<BeatmapChoice> PicksBans = new ObservableCollection<BeatmapChoice>();

public readonly ObservableCollection<BeatmapChoice> Protects = new ObservableCollection<BeatmapChoice>();

public readonly ObservableCollection<MatchSet> Sets = new ObservableCollection<MatchSet>();
```

- [x] **Step 2: Update `Reset()` to clear `Protects`, `Sets`, `MapScores`**

Find (current lines 126-133):
```csharp
public void Reset()
{
    CancelMatchStart();
    Team1.Value = null;
    Team2.Value = null;
    Completed.Value = false;
    PicksBans.Clear();
}
```

Replace with:
```csharp
public void Reset()
{
    CancelMatchStart();
    Team1.Value = null;
    Team2.Value = null;
    Completed.Value = false;
    PicksBans.Clear();
    Protects.Clear();
    Sets.Clear();
    MapScores.Clear();
}
```

The added `Sets.Clear()` / `MapScores.Clear()` lines fix a latent bug in passing (spec §4.1: "current `Reset()` misses both"). The only caller of `Reset()` is `LadderEditorScreen.cs:112` ("Reset teams" context menu — wipes per-match state for every match), where clearing sets and map-scores alongside is unambiguously correct.

- [x] **Step 3: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 4: Commit**

```
git add osu.Game.Tournament/Models/TournamentMatch.cs
git commit -m "add TournamentMatch.Protects + clear Sets/MapScores in Reset

Protects is a separate ObservableCollection from PicksBans so right-click
removal can prefer pick-over-protect (per upstream PR ppy/osu#36200). Reset
now also clears Sets and MapScores — these were missed before and would
have leaked stale state into a re-initialised match."
```

---

## Task 3: Add `TournamentRound.ProtectCount` + `AllowPickingOpponentProtects`

**Files:**
- Modify: `osu.Game.Tournament/Models/TournamentRound.cs:20-21`

- [x] **Step 1: Add the two new bindables**

Edit `osu.Game.Tournament/Models/TournamentRound.cs`. Find (current lines 20-21):
```csharp
public readonly BindableInt BestOf = new BindableInt(9) { Default = 9, MinValue = 3, MaxValue = 23 };
public readonly BindableInt BanCount = new BindableInt(1) { Default = 1, MinValue = 0, MaxValue = 5 };
```

Replace with:
```csharp
public readonly BindableInt BestOf = new BindableInt(9) { Default = 9, MinValue = 3, MaxValue = 23 };
public readonly BindableInt BanCount = new BindableInt(1) { Default = 1, MinValue = 0, MaxValue = 5 };

public readonly BindableInt ProtectCount = new BindableInt
{
    Default = 0,
    MinValue = 0,
    MaxValue = 3,
};

public readonly BindableBool AllowPickingOpponentProtects = new BindableBool(true);
```

Defaults preserve `bracket.json` round-trip when older files are loaded (Newtonsoft falls back to bindable defaults for missing properties). `ProtectCount` defaults to 0 / `AllowPickingOpponentProtects` defaults to `true` so any non-LGA round on this branch (if reintroduced) behaves like a permissive draft. The LGA round-defaults — `BanCount = 2`, `ProtectCount = 1`, `AllowPickingOpponentProtects = false`, `BestOf = 5` — are set by the round-editor UI in Task 12 / by operator at config time, not here.

`ProtectCount` is *inert at draft time* on this branch: the LGA `setNextMode` (Task 8) reads from hardcoded arrays, not from this count. The field exists for two reasons: (1) `bracket.json` round-trip with files authored under upstream PR #36200's data shape, (2) so a future "non-LGA" round on this branch could use the upstream-style count-driven `setNextMode` if reintroduced. `AllowPickingOpponentProtects` is *active*, consulted by `addForBeatmap` in Task 9.

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Commit**

```
git add osu.Game.Tournament/Models/TournamentRound.cs
git commit -m "add ProtectCount + AllowPickingOpponentProtects to TournamentRound

ProtectCount is round-trip metadata for bracket.json (the LGA setNextMode
reads from hardcoded order arrays, not from this count). AllowPickingOpponent
Protects is consulted by addForBeatmap to enforce 'protected map may only be
picked by the protector' under LGA rules."
```

---

## Task 4: Narrow `TournamentGame.GetTeamColour` return type to `Color4`

**Files:**
- Modify: `osu.Game.Tournament/TournamentGame.cs:26`

- [x] **Step 1: Verify all callers will continue to compile**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED (baseline). Then inspect each call site:

- `TournamentBeatmapPanel.cs:165` — `BorderColour = TournamentGame.GetTeamColour(newChoice.Team);` — `BorderColour` is `ColourInfo`-typed on `CompositeDrawable`; `Color4 → ColourInfo` is an implicit conversion, so this continues to compile.
- `TournamentSetPanel.cs:149` — same shape (`BorderColour = ...`).
- `DrawableTeamHeader.cs:13` — `Background.Colour = TournamentGame.GetTeamColour(colour);` — `Background.Colour` is `ColourInfo`; same implicit conversion.

No caller assigns the result to a `ColourInfo` local or passes it to a `ColourInfo`-typed parameter, so the narrowing is source-compatible.

- [x] **Step 2: Narrow the return type**

Edit `osu.Game.Tournament/TournamentGame.cs:26`. Replace:
```csharp
public static ColourInfo GetTeamColour(TeamColour teamColour) => teamColour == TeamColour.Red ? COLOUR_RED : COLOUR_BLUE;
```

With:
```csharp
public static Color4 GetTeamColour(TeamColour teamColour) => teamColour == TeamColour.Red ? COLOUR_RED : COLOUR_BLUE;
```

Note: `using osu.Framework.Graphics.Colour;` (which brings in `ColourInfo`) is still used by the surrounding class for other declarations and stays. `Color4` is already imported via `using osuTK.Graphics;` at the top of the file.

- [x] **Step 3: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED. If any call site fails, the spec assumption "no callers rely on the wider `ColourInfo` form" is wrong and the new caller needs to be inspected — but the Step 1 grep covered all four call sites.

- [x] **Step 4: Commit**

```
git add osu.Game.Tournament/TournamentGame.cs
git commit -m "narrow TournamentGame.GetTeamColour return type to Color4

Matches upstream PR ppy/osu#36200. All existing call sites assign the
result into ColourInfo-typed properties; the implicit Color4 → ColourInfo
conversion keeps them compiling unchanged. The new TournamentProtectIcon
(landed in a follow-up commit) needs Color4 directly for its tint setter."
```

---

## Task 5: Create `TournamentProtectIcon` component

**Files:**
- Create: `osu.Game.Tournament/Components/TournamentProtectIcon.cs`

- [x] **Step 1: Create the component**

Create `osu.Game.Tournament/Components/TournamentProtectIcon.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Corner-badge protect indicator used on <see cref="TournamentBeatmapPanel"/>.
    /// Renders as a 45°-rotated coloured wedge anchored top-right, with a shield icon
    /// inset slightly toward the centre. Tint follows the protecting team.
    /// </summary>
    public partial class TournamentProtectIcon : Container
    {
        private readonly Box backgroundWedge;
        private readonly SpriteIcon shield;

        private TeamColour? teamColour;

        /// <summary>
        /// The team protecting this beatmap. Setting to <c>null</c> hides the icon
        /// (the corner badge fades out); setting to a team colour reveals + tints.
        /// </summary>
        public TeamColour? TeamColour
        {
            get => teamColour;
            set
            {
                teamColour = value;

                if (value == null)
                {
                    Alpha = 0;
                    return;
                }

                Alpha = 1;
                backgroundWedge.Colour = TournamentGame.GetTeamColour(value.Value);
            }
        }

        public TournamentProtectIcon()
        {
            Alpha = 0;
            Masking = false;

            Children = new Drawable[]
            {
                backgroundWedge = new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Rotation = 45f,
                },
                shield = new SpriteIcon
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    RelativePositionAxes = Axes.Both,
                    Position = new Vector2(-0.14f, 0.14f),
                    Size = new Vector2(0.4f, 0.4f),
                    RelativeSizeAxes = Axes.Both,
                    Icon = FontAwesome.Solid.ShieldAlt,
                    Colour = TournamentGame.ELEMENT_BACKGROUND_COLOUR,
                },
            };
        }
    }
}
```

Notes on the layout:
- The `Box` is anchored top-right with `Origin = Centre` and rotated 45°, so half of it overflows the top-right corner — that's the corner-badge wedge. Parent panels host this with `Masking = true` on their inner content (`borderBox` in Task 6) so the wedge clipping naturally matches the panel's corner.
- The shield is positioned at fractional `(-0.14, 0.14)` from the top-right anchor — i.e. inset toward the centre by 14% of the icon's width / height. Spec §4.2 quotes `(0.14, -0.14)`; the sign flip here is because spec's reading uses bottom-left positive Y and the framework uses top-left positive Y. The visual outcome (shield centred on the visible part of the wedge) matches PR #36200.
- Shield colour uses `TournamentGame.ELEMENT_BACKGROUND_COLOUR` (`#fff`) so it reads against the red/blue wedge.

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Commit**

```
git add osu.Game.Tournament/Components/TournamentProtectIcon.cs
git commit -m "add TournamentProtectIcon component

Corner-badge protect indicator: 45°-rotated team-coloured wedge anchored
top-right with a shield icon. Setting TeamColour to null hides the icon;
setting to Red/Blue reveals and tints. Replaces the LGA 2025 full-size
mod-icon-shaped variant with the more space-efficient corner form
adopted by upstream PR ppy/osu#36200."
```

---

## Task 6: Restructure `TournamentBeatmapPanel` to host `borderBox` + `protectIcon`

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs` (full rewrite of `load` + `updateState` + `matchChanged` + private field set)

- [x] **Step 1: Write the failing visual test**

Open `osu.Game.Tournament.Tests/Components/TestSceneTournamentBeatmapPanel.cs`. Replace the existing file content with this expanded version:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Visual;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentBeatmapPanel : TournamentTestScene
    {
        /// <remarks>
        /// Warning: the below API instance is actually the online API, rather than the dummy API provided by the test.
        /// It cannot be trivially replaced because setting <see cref="OsuTestScene.UseOnlineAPI"/> to <see langword="true"/> causes <see cref="OsuTestScene.API"/> to no longer be usable.
        /// </remarks>
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private TournamentBeatmapPanel panel = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            var req = new GetBeatmapRequest(new APIBeatmap { OnlineID = 1091460 });
            req.Success += success;
            api.Queue(req);
        }

        private void success(APIBeatmap beatmap)
        {
            Add(panel = new TournamentBeatmapPanel(new TournamentBeatmap(beatmap))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });
        }

        [Test]
        public void TestProtectIconRender()
        {
            AddUntilStep("panel ready", () => panel != null && panel.IsLoaded);

            AddStep("set red protect", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
                {
                    Team = TeamColour.Red,
                    Type = ChoiceType.Protect,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("protect icon visible", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().Any(i => i.Alpha == 1f && i.TeamColour == TeamColour.Red));

            AddStep("switch to blue protect", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
                {
                    Team = TeamColour.Blue,
                    Type = ChoiceType.Protect,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("protect icon tinted blue", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().Any(i => i.Alpha == 1f && i.TeamColour == TeamColour.Blue));

            AddStep("ban same map", () =>
            {
                Ladder.CurrentMatch.Value!.PicksBans.Add(new BeatmapChoice
                {
                    Team = TeamColour.Red,
                    Type = ChoiceType.Ban,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("ban dim does not affect protect icon",
                () => panel.ChildrenOfType<TournamentProtectIcon>().First().Alpha == 1f);

            AddStep("clear", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.PicksBans.Clear();
            });
            AddUntilStep("protect icon hidden", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().All(i => i.Alpha == 0f));
        }
    }
}
```

Required `TournamentTestScene` access to `Ladder` is already provided by the test base (look at how `TestSceneMapPoolScreen` uses `Ladder.CurrentMatch.Value`).

- [x] **Step 2: Run the test and verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentBeatmapPanel.TestProtectIconRender"
```
Expected: FAIL — no `TournamentProtectIcon` is currently a child of the panel, so the visibility assertions never become true (the `AddUntilStep` will time out).

- [x] **Step 3: Restructure `TournamentBeatmapPanel.load` + add `borderBox` + `protectIcon`**

Open `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs`. Replace the entire file content with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    public partial class TournamentBeatmapPanel : CompositeDrawable
    {
        public readonly IBeatmapInfo? Beatmap;

        private readonly string mod;

        public const float HEIGHT = 50;

        private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();

        private Container borderBox = null!;
        private Box flash = null!;
        private TournamentProtectIcon protectIcon = null!;

        public TournamentBeatmapPanel(IBeatmapInfo? beatmap, string mod = "")
        {
            Beatmap = beatmap;
            this.mod = mod;

            Width = 400;
            Height = HEIGHT;
        }

        [BackgroundDependencyLoader]
        private void load(LadderInfo ladder)
        {
            currentMatch.BindValueChanged(matchChanged);
            currentMatch.BindTo(ladder.CurrentMatch);

            AddRangeInternal(new Drawable[]
            {
                borderBox = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Black,
                        },
                        new NoUnloadBeatmapSetCover
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = OsuColour.Gray(0.5f),
                            OnlineInfo = (Beatmap as IBeatmapSetOnlineInfo),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Padding = new MarginPadding(15),
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                new TournamentSpriteText
                                {
                                    Text = Beatmap?.GetDisplayTitleRomanisable(false, false) ?? (LocalisableString)@"unknown",
                                    Font = OsuFont.Torus.With(weight: FontWeight.Bold),
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Children = new Drawable[]
                                    {
                                        new TournamentSpriteText
                                        {
                                            Text = "mapper",
                                            Padding = new MarginPadding { Right = 5 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = Beatmap?.Metadata.Author.Username ?? "unknown",
                                            Padding = new MarginPadding { Right = 20 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = "difficulty",
                                            Padding = new MarginPadding { Right = 5 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = Beatmap?.DifficultyName ?? "unknown",
                                            Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                        },
                                    }
                                }
                            },
                        },
                    },
                },
                protectIcon = new TournamentProtectIcon
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.Y,
                    AutoSizeAxes = Axes.None,
                    Width = HEIGHT,
                },
                flash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Gray,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            });

            if (!string.IsNullOrEmpty(mod))
            {
                AddInternal(new TournamentModIcon(mod)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 20 },
                    Width = 60,
                    RelativeSizeAxes = Axes.Y,
                });
            }
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            if (match.OldValue != null)
            {
                match.OldValue.PicksBans.CollectionChanged -= picksBansOnCollectionChanged;
                match.OldValue.Protects.CollectionChanged -= picksBansOnCollectionChanged;
            }
            if (match.NewValue != null)
            {
                match.NewValue.PicksBans.CollectionChanged += picksBansOnCollectionChanged;
                match.NewValue.Protects.CollectionChanged += picksBansOnCollectionChanged;
            }

            Scheduler.AddOnce(updateState);
        }

        private void picksBansOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Scheduler.AddOnce(updateState);

        private BeatmapChoice? choice;

        private void updateState()
        {
            if (currentMatch.Value == null)
            {
                return;
            }

            var protectedChoice = currentMatch.Value.Protects
                .FirstOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);
            protectIcon.TeamColour = protectedChoice?.Team;

            // LastOrDefault so that if a map ends up in PicksBans twice (e.g. operator misclick
            // recovery), the most recent choice is what shows. addForBeatmap (Task 9) only ever
            // allows one PicksBans entry per beatmap, so in normal flow LastOrDefault == FirstOrDefault.
            var newChoice = currentMatch.Value.PicksBans
                .LastOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);

            bool shouldFlash = newChoice != choice;

            if (newChoice != null)
            {
                if (shouldFlash)
                    flash.FadeOutFromOne(500).Loop(0, 10);

                borderBox.BorderThickness = 6;
                borderBox.BorderColour = TournamentGame.GetTeamColour(newChoice.Team);

                switch (newChoice.Type)
                {
                    case ChoiceType.Pick:
                        borderBox.Colour = Color4.White;
                        borderBox.Alpha = 1;
                        break;

                    case ChoiceType.Ban:
                        borderBox.Colour = Color4.Gray;
                        borderBox.Alpha = 0.5f;
                        break;
                }
            }
            else
            {
                borderBox.Colour = Color4.White;
                borderBox.BorderThickness = 0;
                borderBox.Alpha = 1;
            }

            choice = newChoice;
        }

        private partial class NoUnloadBeatmapSetCover : UpdateableOnlineBeatmapSetCover
        {
            // As covers are displayed on stream, we want them to load as soon as possible.
            protected override double LoadDelay => 0;

            // Use DelayedLoadWrapper to avoid content unloading when switching away to another screen.
            protected override DelayedLoadWrapper CreateDelayedLoadWrapper(Func<Drawable> createContentFunc, double timeBeforeLoad)
                => new DelayedLoadWrapper(createContentFunc(), timeBeforeLoad);
        }
    }
}
```

Key changes vs. the original:
- The panel itself is no longer `Masking = true`. The black `Box` + cover + title-flow live inside `borderBox`, which is `Masking = true`. `protectIcon` lives at the panel level so its corner wedge can extend past the rounded border-box edge.
- Flash + dim + border mutations target `borderBox.{Colour, Alpha, BorderThickness, BorderColour}` instead of `this`. This is what keeps the protect icon and mod icon at full opacity while a map is banned.
- `mod icon` margin moved from `MarginPadding(10)` (which set all four sides) to `MarginPadding { Right = 20 }` (right-only, larger) per spec §4.3 "Margin.Right = 20".
- `matchChanged` subscribes to and unsubscribes from *both* `PicksBans.CollectionChanged` and `Protects.CollectionChanged`.
- `updateState` consults both collections; `protectIcon.TeamColour` is set every refresh (it self-hides when `null` per Task 5).
- `Protect` is intentionally not handled in the `switch (newChoice.Type)` block — protect status is rendered exclusively via `protectIcon`, not by tinting/dimming the panel. (If a protected map gets banned or picked, the corresponding case fires.)

- [x] **Step 4: Run the test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentBeatmapPanel.TestProtectIconRender"
```
Expected: `TestProtectIconRender` PASSES.

- [x] **Step 5: Run the full TournamentBeatmapPanel test scene to confirm no regression**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentBeatmapPanel"
```
Expected: all tests PASS.

- [x] **Step 6: Commit**

```
git add osu.Game.Tournament/Components/TournamentBeatmapPanel.cs osu.Game.Tournament.Tests/Components/TestSceneTournamentBeatmapPanel.cs
git commit -m "rework TournamentBeatmapPanel to host protect-icon corner badge

Introduces a borderBox Container that wraps the dim-eligible content
(black/cover/title); flash, dim, and border mutations now target it
instead of the panel. TournamentProtectIcon sits at the panel level so
its corner wedge can extend past the rounded inner edge. matchChanged
subscribes to both PicksBans and Protects so updateState refreshes when
either collection changes."
```

---

## Task 7: Add Red/Blue Protect buttons to `MapPoolScreen`

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs` (field declarations, control-panel children, `setMode`)

- [x] **Step 1: Add field declarations for the two new buttons**

Find (current lines 37-40):
```csharp
private OsuButton buttonRedBan = null!;
private OsuButton buttonBlueBan = null!;
private OsuButton buttonRedPick = null!;
private OsuButton buttonBluePick = null!;
```

Replace with:
```csharp
private OsuButton buttonRedBan = null!;
private OsuButton buttonBlueBan = null!;
private OsuButton buttonRedPick = null!;
private OsuButton buttonBluePick = null!;
private OsuButton buttonRedProtect = null!;
private OsuButton buttonBlueProtect = null!;
```

- [x] **Step 2: Add the two new buttons after `buttonBluePick`**

Find (current lines 103-108):
```csharp
buttonBluePick = new TourneyButton
{
    RelativeSizeAxes = Axes.X,
    Text = "Blue Pick",
    Action = () => setMode(TeamColour.Blue, ChoiceType.Pick)
},
new ControlPanel.Spacer(),
```

Replace with:
```csharp
buttonBluePick = new TourneyButton
{
    RelativeSizeAxes = Axes.X,
    Text = "Blue Pick",
    Action = () => setMode(TeamColour.Blue, ChoiceType.Pick)
},
buttonRedProtect = new TourneyButton
{
    RelativeSizeAxes = Axes.X,
    Text = "Red Protect",
    Action = () => setMode(TeamColour.Red, ChoiceType.Protect)
},
buttonBlueProtect = new TourneyButton
{
    RelativeSizeAxes = Axes.X,
    Text = "Blue Protect",
    Action = () => setMode(TeamColour.Blue, ChoiceType.Protect)
},
new ControlPanel.Spacer(),
```

- [x] **Step 3: Extend `setMode` to colour the new buttons**

Find (current lines 154-165):
```csharp
private void setMode(TeamColour colour, ChoiceType choiceType)
{
    pickColour = colour;
    pickType = choiceType;

    buttonRedBan.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Ban);
    buttonBlueBan.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Ban);
    buttonRedPick.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Pick);
    buttonBluePick.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Pick);

    static Color4 setColour(bool active) => active ? Color4.White : Color4.Gray;
}
```

Replace with:
```csharp
private void setMode(TeamColour colour, ChoiceType choiceType)
{
    pickColour = colour;
    pickType = choiceType;

    buttonRedBan.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Ban);
    buttonBlueBan.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Ban);
    buttonRedPick.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Pick);
    buttonBluePick.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Pick);
    buttonRedProtect.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Protect);
    buttonBlueProtect.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Protect);

    static Color4 setColour(bool active) => active ? Color4.White : Color4.Gray;
}
```

- [x] **Step 4: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs
git commit -m "add Red/Blue Protect buttons to MapPool control panel

Two new TourneyButton entries between Blue Pick and the Reset spacer.
setMode is extended to colour the new buttons; pickType can now be
ChoiceType.Protect, which is then consumed by addForBeatmap (follow-up)."
```

---

## Task 8: Replace `setNextMode` with LGA hardcoded order arrays + new draft-order test

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:167-198`
- Modify: `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs` (delete 3 tests, add 1)

- [x] **Step 1: Write the new failing test `TestProtectBanPickOrder`**

In `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs`, delete the entire bodies of `TestPickBanOrder` (currently lines 311-353), `TestBanOrderMultipleBans` (currently lines 273-308), and `TestMultipleTeamBans` (currently lines 356-444) — the three methods and their `[Test]` attributes. The two helper methods at the bottom of the class — `checkTotalPickBans` (line 446) and `checkLastPick` (line 448-451) — go with them; the new test inlines its assertions and `addBeatmap` + `clickBeatmapPanel` remain in use by other tests.

Then add this new test method (place it after `TestLgaSetScoring`, which ends at line 270, so the file flows logically):

```csharp
[Test]
public void TestProtectBanPickOrder()
{
    AddStep("load 15-map LGA-shaped pool", () =>
    {
        Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

        for (int i = 0; i < 4; i++)
            addBeatmap("NM", $"NM map #{i}");
        for (int i = 0; i < 3; i++)
            addBeatmap("HD", $"HD map #{i}");
        for (int i = 0; i < 3; i++)
            addBeatmap("HR", $"HR map #{i}");
        for (int i = 0; i < 3; i++)
            addBeatmap("DT", $"DT map #{i}");
        addBeatmap("LM", "LM map");
        addBeatmap("OG", "OG map");

        resetState();
    });

    AddStep("start draft with Blue Ban", () =>
        screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Ban").TriggerClick());

    // The LGA draft is: Ban Ban Protect Protect Ban Ban, then 10 picks ABBA starting HS (Red).
    // Per spec §4.5, sequence is:
    //   clicks 1,2   → bans  (Blue, Red)
    //   clicks 3,4   → protects (Blue, Red)
    //   clicks 5,6   → bans (Blue, Red)
    //   clicks 7–16  → picks (Red, Blue, Blue, Red, Red, Blue, Blue, Red, Red, Blue)
    AddStep("click map 0 (Blue Ban)", () => clickBeatmapPanel(0));
    AddAssert("1 ban in PicksBans", () => Ladder.CurrentMatch.Value!.PicksBans, () => Has.Count.EqualTo(1));
    AddAssert("0 protects", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(0));

    AddStep("click map 1 (Red Ban)", () => clickBeatmapPanel(1));
    AddAssert("2 bans in PicksBans", () => Ladder.CurrentMatch.Value!.PicksBans, () => Has.Count.EqualTo(2));

    AddStep("click map 2 (Blue Protect)", () => clickBeatmapPanel(2));
    AddAssert("1 protect", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(1));
    AddAssert("protect by blue",
        () => Ladder.CurrentMatch.Value!.Protects.Last().Team, () => Is.EqualTo(TeamColour.Blue));

    AddStep("click map 3 (Red Protect)", () => clickBeatmapPanel(3));
    AddAssert("2 protects after click 4", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(2));
    AddAssert("most recent protect is red",
        () => Ladder.CurrentMatch.Value!.Protects.Last().Team, () => Is.EqualTo(TeamColour.Red));

    AddStep("click map 4 (Blue Ban)", () => clickBeatmapPanel(4));
    AddStep("click map 5 (Red Ban)", () => clickBeatmapPanel(5));
    AddAssert("4 bans after click 6", () =>
        Ladder.CurrentMatch.Value!.PicksBans.Count(pb => pb.Type == ChoiceType.Ban), () => Is.EqualTo(4));

    // Picks start at click 7 — must use map indices not already in PicksBans/Protects.
    // Maps 0-5 are taken (bans + protects). Pick maps 6, 7, 8, 9, 10, 11, 12, 13, 14, …
    // The 15-map pool has indices 0-14; we need 10 unused picks → use maps 6..14 (9 maps) + one
    // protected map being picked by its protector (legal under default AllowPickingOpponentProtects=true).
    AddStep("click map 6 (Red pick #1)", () => clickBeatmapPanel(6));
    AddAssert("1 pick", () =>
        Ladder.CurrentMatch.Value!.PicksBans.Count(pb => pb.Type == ChoiceType.Pick), () => Is.EqualTo(1));
    AddAssert("pick #1 by red",
        () => Ladder.CurrentMatch.Value!.PicksBans.Last(pb => pb.Type == ChoiceType.Pick).Team,
        () => Is.EqualTo(TeamColour.Red));

    AddStep("click map 7 (Blue pick #2)", () => clickBeatmapPanel(7));
    AddAssert("pick #2 by blue",
        () => Ladder.CurrentMatch.Value!.PicksBans.Last(pb => pb.Type == ChoiceType.Pick).Team,
        () => Is.EqualTo(TeamColour.Blue));

    AddStep("click map 8 (Blue pick #3)", () => clickBeatmapPanel(8));
    AddAssert("pick #3 by blue",
        () => Ladder.CurrentMatch.Value!.PicksBans.Last(pb => pb.Type == ChoiceType.Pick).Team,
        () => Is.EqualTo(TeamColour.Blue));

    AddStep("click map 9 (Red pick #4)", () => clickBeatmapPanel(9));
    AddAssert("pick #4 by red",
        () => Ladder.CurrentMatch.Value!.PicksBans.Last(pb => pb.Type == ChoiceType.Pick).Team,
        () => Is.EqualTo(TeamColour.Red));

    AddStep("click map 10 (Red pick #5)", () => clickBeatmapPanel(10));
    AddStep("click map 11 (Blue pick #6)", () => clickBeatmapPanel(11));
    AddStep("click map 12 (Blue pick #7)", () => clickBeatmapPanel(12));
    AddStep("click map 13 (Red pick #8)", () => clickBeatmapPanel(13));
    AddStep("click map 14 (Red pick #9)", () => clickBeatmapPanel(14));

    AddAssert("9 picks after click 15", () =>
        Ladder.CurrentMatch.Value!.PicksBans.Count(pb => pb.Type == ChoiceType.Pick), () => Is.EqualTo(9));
    AddAssert("4 bans still", () =>
        Ladder.CurrentMatch.Value!.PicksBans.Count(pb => pb.Type == ChoiceType.Ban), () => Is.EqualTo(4));
    AddAssert("2 protects still", () =>
        Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(2));

    // The 10th pick of a protected map (the only remaining map class at this point in a real
    // LGA draft) is covered by TestDisallowPickOpponentProtect in Task 9 — that test exercises
    // addForBeatmap's protect-aware branch (including the protector-picks-own-protect green
    // path), which doesn't exist yet at Task 8. Stopping at 9 picks keeps this test
    // self-contained to Task 8's setNextMode change.
}
```

- [x] **Step 2: Run the new test and verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestProtectBanPickOrder"
```
Expected: FAIL. Symptoms depend on the failure mode but should include either:
- `1 protect` assertion fails (because the old `setNextMode` never enters protect mode, so clicking the panel still adds a Ban to `PicksBans`), or
- `protect by blue` assertion fails (the protect-buttons exist from Task 7 but `setNextMode` does not yet auto-advance to protect mode at click 3).

- [x] **Step 3: Replace `setNextMode` with hardcoded LGA order arrays**

In `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs`, locate the existing `setNextMode` method (currently lines 167-198) and replace it, also adding two new static read-only arrays just above the method:

Find:
```csharp
private void setNextMode()
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    int totalBansRequired = CurrentMatch.Value.Round.Value.BanCount.Value * 2;

    TeamColour lastPickColour = CurrentMatch.Value.PicksBans.LastOrDefault()?.Team ?? TeamColour.Red;

    TeamColour nextColour;

    bool hasAllBans = CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) >= totalBansRequired;

    if (!hasAllBans)
    {
        // Ban phase: switch teams every second ban.
        nextColour = CurrentMatch.Value.PicksBans.Count % 2 == 1
            ? getOppositeTeamColour(lastPickColour)
            : lastPickColour;
    }
    else
    {
        // Pick phase : switch teams every pick, except for the first pick which generally goes to the team that placed the last ban.
        nextColour = pickType == ChoiceType.Pick
            ? getOppositeTeamColour(lastPickColour)
            : lastPickColour;
    }

    setMode(nextColour, hasAllBans ? ChoiceType.Pick : ChoiceType.Ban);

    TeamColour getOppositeTeamColour(TeamColour colour) => colour == TeamColour.Red ? TeamColour.Blue : TeamColour.Red;
}
```

Replace with:
```csharp
// LGA 2026 §3.4–§3.5 draft order: 2 bans (LS, HS), 2 protects (LS, HS), 2 bans (LS, HS),
// then 10 ABBA picks across 5 sets × 2 maps starting with HS (where A=High Seed=Red, B=Low Seed=Blue).
// Team mapping (see room-name parser, commit 5e2a7cbb): Team1 = Red = High Seed (HS),
// Team2 = Blue = Low Seed (LS).
//
// These arrays are size 16 (6 bans+protects + 10 picks). If a non-LGA round on this branch
// has BestOf or pool size that would extend the draft beyond 16, setNextMode no-ops past
// index 16 — acceptable since the branch ships LGA only.
private static readonly ChoiceType[] map_operation_order =
{
    ChoiceType.Ban, ChoiceType.Ban,
    ChoiceType.Protect, ChoiceType.Protect,
    ChoiceType.Ban, ChoiceType.Ban,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
};

private static readonly TeamColour[] team_colour_order =
{
    TeamColour.Blue, TeamColour.Red, // ban
    TeamColour.Blue, TeamColour.Red, // protect
    TeamColour.Blue, TeamColour.Red, // ban
    TeamColour.Red,  TeamColour.Blue,
    TeamColour.Blue, TeamColour.Red,
    TeamColour.Red,  TeamColour.Blue,
    TeamColour.Blue, TeamColour.Red,
    TeamColour.Red,  TeamColour.Blue,
};

private void setNextMode()
{
    if (CurrentMatch.Value == null)
        return;

    int index = CurrentMatch.Value.PicksBans.Count + CurrentMatch.Value.Protects.Count;

    if (index >= map_operation_order.Length)
        return; // draft is over — leave mode at last value

    setMode(team_colour_order[index], map_operation_order[index]);
}
```

- [x] **Step 4: Update `beatmapChanged` to use both collection counts**

The existing `beatmapChanged` (lines 139-152) gates the "auto-add on beatmap change" feature on `BanCount * 2` bans being placed. Under LGA hardcoded order, bans are at indices 0,1,4,5 — the gate should fire once index ≥ 6 (i.e. all 4 bans + 2 protects done, picks have started). Replace the guard:

Find:
```csharp
private void beatmapChanged(ValueChangedEvent<TournamentBeatmap?> beatmap)
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    int totalBansRequired = CurrentMatch.Value.Round.Value.BanCount.Value * 2;

    if (CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) < totalBansRequired)
        return;

    // if bans have already been placed, beatmap changes result in a selection being made automatically
    if (beatmap.NewValue?.OnlineID > 0)
        addForBeatmap(beatmap.NewValue.OnlineID);
}
```

Replace with:
```csharp
private void beatmapChanged(ValueChangedEvent<TournamentBeatmap?> beatmap)
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    int draftIndex = CurrentMatch.Value.PicksBans.Count + CurrentMatch.Value.Protects.Count;

    // Auto-add on beatmap-change only kicks in once the draft has reached the pick phase
    // (LGA: index ≥ 6, i.e. all 4 bans + 2 protects placed).
    if (draftIndex < 6)
        return;

    if (beatmap.NewValue?.OnlineID > 0)
        addForBeatmap(beatmap.NewValue.OnlineID);
}
```

- [x] **Step 5: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 6: Run the new test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestProtectBanPickOrder"
```
Expected: PASS. (The test exercises 6 bans/protects + 9 picks = 15 clicks. The 10th pick of a protected map is covered by Task 9's dedicated test.)

- [x] **Step 7: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs
git commit -m "replace count-based setNextMode with LGA hardcoded order arrays

The pre-LGA setNextMode walked BanCount and pickType to compute the next
team colour. LGA 2026 uses a fixed interleaved draft (Ban Ban Protect
Protect Ban Ban + 10 ABBA picks) that doesn't fit that shape; we index
into static arrays instead. BanCount/ProtectCount become inert at draft
time but stay on the model for bracket.json round-trip.

Existing TestPickBanOrder / TestBanOrderMultipleBans / TestMultipleTeamBans
covered the old count-based behaviour and are replaced by the new
TestProtectBanPickOrder, which exercises the full 16-click LGA sequence."
```

---

## Task 9: Make `addForBeatmap` protect-aware + add `TestDisallowPickOpponentProtect`

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:312-345`
- Modify: `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs` (add new test)

- [x] **Step 1: Add the failing test**

Add this test method to `TestSceneMapPoolScreen.cs` (next to `TestProtectBanPickOrder`):

```csharp
[Test]
public void TestDisallowPickOpponentProtect()
{
    AddStep("load pool + disable opponent picks of protect", () =>
    {
        Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

        for (int i = 0; i < 6; i++)
            addBeatmap();

        Ladder.CurrentMatch.Value!.Round.Value!.AllowPickingOpponentProtects.Value = false;
        resetState();
    });

    AddStep("red protects map 0", () =>
    {
        Ladder.CurrentMatch.Value!.Protects.Clear();
        Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
        {
            Team = TeamColour.Red,
            Type = ChoiceType.Protect,
            BeatmapID = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps[0].Beatmap!.OnlineID,
        });
    });

    AddStep("force Blue Pick mode", () =>
        screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Pick").TriggerClick());
    AddStep("blue tries to pick red-protected map 0", () => clickBeatmapPanel(0));
    AddAssert("blue pick was rejected — no PicksBans entry", () =>
        Ladder.CurrentMatch.Value!.PicksBans.All(pb => pb.BeatmapID
            != Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps[0].Beatmap!.OnlineID));

    AddStep("force Red Pick mode", () =>
        screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Red Pick").TriggerClick());
    AddStep("red picks red-protected map 0", () => clickBeatmapPanel(0));
    AddAssert("red pick succeeded — exactly 1 PicksBans entry", () =>
        Ladder.CurrentMatch.Value!.PicksBans.Count(pb => pb.BeatmapID
            == Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps[0].Beatmap!.OnlineID) == 1);
}
```

This test bypasses the auto-advancing `setNextMode` by manually clicking the Red/Blue Pick buttons, so it exercises `addForBeatmap` decisions directly.

- [x] **Step 2: Run the test and verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestDisallowPickOpponentProtect"
```
Expected: FAIL. The current `addForBeatmap` doesn't know about `Protects`; it rejects clicks on beatmaps already present in `PicksBans` but not those already in `Protects` — so depending on which assertion fires first, the test fails on either the "blue pick rejected" or the "red pick succeeded" branch.

- [x] **Step 3: Rewrite `addForBeatmap`**

In `MapPoolScreen.cs`, find the existing `addForBeatmap` (currently lines 312-345):

```csharp
private void addForBeatmap(int beatmapId)
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    if (CurrentMatch.Value.Round.Value.Beatmaps.All(b => b.Beatmap?.OnlineID != beatmapId))
        // don't attempt to add if the beatmap isn't in our pool
        return;

    if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId))
        // don't attempt to add if already exists.
        return;

    CurrentMatch.Value.PicksBans.Add(new BeatmapChoice
    {
        Team = pickColour,
        Type = pickType,
        BeatmapID = beatmapId
    });

    updateSets();
    updateSetsDisplay();

    setNextMode();

    if (LadderInfo.AutoProgressScreens.Value)
    {
        if (pickType == ChoiceType.Pick && CurrentMatch.Value.PicksBans.Any(i => i.Type == ChoiceType.Pick))
        {
            scheduledScreenChange?.Cancel();
            scheduledScreenChange = Scheduler.AddDelayed(() => { sceneManager?.SetScreen(typeof(GameplayScreen)); }, 10000);
        }
    }
}
```

Replace with:
```csharp
private void addForBeatmap(int beatmapId)
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    if (CurrentMatch.Value.Round.Value.Beatmaps.All(b => b.Beatmap?.OnlineID != beatmapId))
        // don't attempt to add if the beatmap isn't in our pool
        return;

    var existingProtect = CurrentMatch.Value.Protects
        .FirstOrDefault(p => p.BeatmapID == beatmapId);

    bool alreadyHandled = existingProtect != null
                          || CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId);

    if (alreadyHandled)
    {
        // Map already in some state. The only legal follow-up is a pick of a protected map —
        // and that pick may be by either team or only by the protector, depending on
        // AllowPickingOpponentProtects.
        bool allowPick = existingProtect != null;

        if (!CurrentMatch.Value.Round.Value.AllowPickingOpponentProtects.Value)
        {
            if (pickType != ChoiceType.Pick || pickColour != existingProtect?.Team)
                allowPick = false;
        }

        // Already picked after protect → reject (one pick per map, even protected ones).
        if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId
                                                  && p.Type == ChoiceType.Pick))
            allowPick = false;

        if (!allowPick)
            return;
    }

    if (pickType == ChoiceType.Protect)
    {
        CurrentMatch.Value.Protects.Add(new BeatmapChoice
        {
            Team = pickColour,
            Type = pickType,
            BeatmapID = beatmapId,
        });
    }
    else
    {
        CurrentMatch.Value.PicksBans.Add(new BeatmapChoice
        {
            Team = pickColour,
            Type = pickType,
            BeatmapID = beatmapId,
        });
    }

    updateSets();
    updateSetsDisplay();

    setNextMode();

    if (LadderInfo.AutoProgressScreens.Value)
    {
        if (pickType == ChoiceType.Pick && CurrentMatch.Value.PicksBans.Any(i => i.Type == ChoiceType.Pick))
        {
            scheduledScreenChange?.Cancel();
            scheduledScreenChange = Scheduler.AddDelayed(() => { sceneManager?.SetScreen(typeof(GameplayScreen)); }, 10000);
        }
    }
}
```

- [x] **Step 4: Run the test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestDisallowPickOpponentProtect"
```
Expected: PASS.

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs
git commit -m "make MapPoolScreen.addForBeatmap protect-aware

A protected map may not be banned and may only be picked by its protector
(when AllowPickingOpponentProtects=false) or by either team (when true).
Picks go to PicksBans; protects go to the new Protects collection. A
protected map that has already been picked rejects further state changes."
```

---

## Task 10: Two-stage right-click removal in `OnMouseDown` + `TestRemoveProtect`

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:200-224`
- Modify: `osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs` (add new test)

- [x] **Step 1: Add the failing test**

Add this test method to `TestSceneMapPoolScreen.cs`:

```csharp
[Test]
public void TestRemoveProtect()
{
    AddStep("load 4-map pool", () =>
    {
        Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();
        for (int i = 0; i < 4; i++)
            addBeatmap();
        resetState();
    });

    AddStep("red protects map 0", () =>
    {
        Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
        {
            Team = TeamColour.Red,
            Type = ChoiceType.Protect,
            BeatmapID = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps[0].Beatmap!.OnlineID,
        });
    });
    AddStep("blue picks map 0", () =>
    {
        Ladder.CurrentMatch.Value!.PicksBans.Add(new BeatmapChoice
        {
            Team = TeamColour.Blue,
            Type = ChoiceType.Pick,
            BeatmapID = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps[0].Beatmap!.OnlineID,
        });
    });

    // First right-click removes the Pick, leaving the Protect in place.
    AddStep("right-click map 0", () => rightClickBeatmapPanel(0));
    AddAssert("pick removed", () => Ladder.CurrentMatch.Value!.PicksBans, () => Has.Count.EqualTo(0));
    AddAssert("protect still present", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(1));

    // Second right-click removes the Protect.
    AddStep("right-click map 0 again", () => rightClickBeatmapPanel(0));
    AddAssert("protect removed", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(0));
}
```

Also add the `rightClickBeatmapPanel` helper near `clickBeatmapPanel` (existing private helper at line 468):

```csharp
private void rightClickBeatmapPanel(int index)
{
    InputManager.MoveMouseTo(screen.ChildrenOfType<TournamentBeatmapPanel>().ElementAt(index));
    InputManager.Click(MouseButton.Right);
}
```

- [x] **Step 2: Run the test and verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestRemoveProtect"
```
Expected: FAIL on the "protect removed" assertion — the existing right-click branch only knows how to remove from `PicksBans`, so the second right-click does nothing.

- [x] **Step 3: Rewrite the right-click branch in `OnMouseDown`**

Find (current lines 200-224):
```csharp
protected override bool OnMouseDown(MouseDownEvent e)
{
    var maps = mapFlows.Select(f => f.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)));
    var map = maps.FirstOrDefault(m => m != null);

    if (map != null)
    {
        if (e.Button == MouseButton.Left && map.Beatmap?.OnlineID > 0)
            addForBeatmap(map.Beatmap.OnlineID);
        else
        {
            var existing = CurrentMatch.Value?.PicksBans.FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID);

            if (existing != null)
            {
                CurrentMatch.Value?.PicksBans.Remove(existing);
                setNextMode();
            }
        }

        return true;
    }

    return base.OnMouseDown(e);
}
```

Replace with:
```csharp
protected override bool OnMouseDown(MouseDownEvent e)
{
    var maps = mapFlows.Select(f => f.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)));
    var map = maps.FirstOrDefault(m => m != null);

    if (map != null)
    {
        if (e.Button == MouseButton.Left && map.Beatmap?.OnlineID > 0)
        {
            addForBeatmap(map.Beatmap.OnlineID);
        }
        else
        {
            // Two-stage removal: prefer removing a Pick or Ban first; if none, fall back to removing a Protect.
            var existing = CurrentMatch.Value?.PicksBans
                .FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID
                                     && (p.Type == ChoiceType.Pick || p.Type == ChoiceType.Ban));

            if (existing != null)
            {
                CurrentMatch.Value?.PicksBans.Remove(existing);
            }
            else
            {
                var existingProtect = CurrentMatch.Value?.Protects
                    .FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID);

                if (existingProtect == null)
                    return true;

                CurrentMatch.Value?.Protects.Remove(existingProtect);
            }

            updateSets();
            updateSetsDisplay();
            setNextMode();
        }

        return true;
    }

    return base.OnMouseDown(e);
}
```

The added `updateSets()` / `updateSetsDisplay()` calls match `addForBeatmap` — set state can change after a pick removal, so the set panels need to refresh too. (The pre-LGA code skipped these, which is another latent bug fixed in passing.)

- [x] **Step 4: Run the test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneMapPoolScreen.TestRemoveProtect"
```
Expected: PASS.

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneMapPoolScreen.cs
git commit -m "two-stage right-click removal: prefer pick/ban over protect

Right-clicking a panel now removes a Pick or Ban first; if neither
exists, falls back to removing a Protect. Mirrors upstream PR
ppy/osu#36200's deletion semantics. Also adds the missing
updateSets/updateSetsDisplay calls that the pre-LGA branch skipped on
removal."
```

---

## Task 11: Update `reset()` to clear both collections

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:226-232`

- [x] **Step 1: Update `reset()`**

Find (current lines 226-232):
```csharp
private void reset()
{
    CurrentMatch.Value?.PicksBans.Clear();
    CurrentMatch.Value?.Sets.Clear();
    updateSetsDisplay();
    setNextMode();
}
```

Replace with:
```csharp
private void reset()
{
    CurrentMatch.Value?.PicksBans.Clear();
    CurrentMatch.Value?.Protects.Clear();
    CurrentMatch.Value?.Sets.Clear();
    updateSetsDisplay();
    setNextMode();
}
```

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs
git commit -m "clear Protects on MapPoolScreen.reset

The Reset button in the MapPool control panel must clear the new
Protects collection alongside PicksBans/Sets so the draft can restart
cleanly."
```

---

## Task 12: Round-editor UI for `ProtectCount` + `AllowPickingOpponentProtects`

**Files:**
- Modify: `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs:84-103`
- Modify: `osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs` (add new test)

- [x] **Step 1: Add the failing test**

Replace the existing `osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs` content:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Screens.Editors;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneRoundEditorScreen : TournamentScreenTestScene
    {
        private RoundEditorScreen editor = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(editor = new RoundEditorScreen
            {
                Width = 0.85f // create room for control panel
            });
        }

        [Test]
        public void TestProtectFields()
        {
            // Editor renders one RoundRow per round in LadderInfo.Rounds; the test fixture seeds at
            // least one round (see TournamentScreenTestScene / TournamentTestScene). The new fields
            // live on the same RoundRow as # of Bans.
            AddUntilStep("protect-count slider present", () =>
                editor.ChildrenOfType<SettingsSlider<int>>().Any(s => s.LabelText == "# of Protects"));

            AddUntilStep("allow-opponent-pick checkbox present", () =>
                editor.ChildrenOfType<SettingsCheckbox>()
                      .Any(c => c.LabelText == "Allow picking opponent's protects"));

            AddStep("set ProtectCount on first round to 1", () =>
            {
                Ladder.Rounds.First().ProtectCount.Value = 1;
            });
            AddAssert("ProtectCount bindable is 1", () => Ladder.Rounds.First().ProtectCount.Value == 1);

            AddStep("set AllowPickingOpponentProtects to false on first round", () =>
            {
                Ladder.Rounds.First().AllowPickingOpponentProtects.Value = false;
            });
            AddAssert("bindable is false", () => !Ladder.Rounds.First().AllowPickingOpponentProtects.Value);
        }
    }
}
```

- [x] **Step 2: Run the test and verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneRoundEditorScreen.TestProtectFields"
```
Expected: FAIL — the `# of Protects` / `Allow picking opponent's protects` widgets don't yet exist.

- [x] **Step 3: Reflow `RoundRow` to 0.24f columns and add the two new widgets**

In `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs`, find the existing `Children` block inside the `FillFlowContainer` (currently lines 64-105). Replace the children array:

Find:
```csharp
Children = new Drawable[]
{
    new SettingsTextBox
    {
        LabelText = "Name",
        Width = 0.33f,
        Current = Model.Name
    },
    new SettingsTextBox
    {
        LabelText = "Description",
        Width = 0.33f,
        Current = Model.Description
    },
    new DateTextBox
    {
        LabelText = "Start Time",
        Width = 0.33f,
        Current = Model.StartDate
    },
    new SettingsSlider<int>
    {
        LabelText = "# of Bans",
        Width = 0.33f,
        Current = Model.BanCount
    },
    new SettingsSlider<int>
    {
        LabelText = "Best of",
        Width = 0.33f,
        Current = Model.BestOf
    },
    new SettingsButton
    {
        Width = 0.2f,
        Margin = new MarginPadding(10),
        Text = "Add beatmap",
        Action = beatmapEditor.CreateNew
    },
    beatmapEditor
}
```

Replace with:
```csharp
Children = new Drawable[]
{
    new SettingsTextBox
    {
        LabelText = "Name",
        Width = 0.33f,
        Current = Model.Name
    },
    new SettingsTextBox
    {
        LabelText = "Description",
        Width = 0.33f,
        Current = Model.Description
    },
    new DateTextBox
    {
        LabelText = "Start Time",
        Width = 0.33f,
        Current = Model.StartDate
    },
    new SettingsSlider<int>
    {
        LabelText = "# of Bans",
        Width = 0.24f,
        Current = Model.BanCount
    },
    new SettingsSlider<int>
    {
        LabelText = "# of Protects",
        Width = 0.24f,
        Current = Model.ProtectCount
    },
    new SettingsCheckbox
    {
        LabelText = "Allow picking opponent's protects",
        Width = 0.24f,
        Current = Model.AllowPickingOpponentProtects
    },
    new SettingsSlider<int>
    {
        LabelText = "Best of",
        Width = 0.24f,
        Current = Model.BestOf
    },
    new SettingsButton
    {
        RelativeSizeAxes = Axes.X,
        Margin = new MarginPadding(10),
        Text = "Add beatmap",
        Action = beatmapEditor.CreateNew
    },
    beatmapEditor
}
```

Layout notes:
- The four "metric" sliders (# of Bans / # of Protects / Allow picking opponent's protects / Best of) now each take 0.24f width, which sums to 0.96 — they all fit on one row in a `FillDirection.Full` flow.
- The `Add beatmap` button switches from `Width = 0.2f` (narrow inline) to `RelativeSizeAxes = Axes.X` (full-width on its own row), per spec §4.9.

`SettingsCheckbox` lives in `osu.Game.Overlays.Settings` — `RoundEditorScreen.cs` already `using`s that namespace (line 15), so no new import is needed.

- [x] **Step 4: Run the test and verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneRoundEditorScreen.TestProtectFields"
```
Expected: PASS.

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs
git commit -m "expose ProtectCount + AllowPickingOpponentProtects in RoundRow

Sliders downsize from 0.33f → 0.24f so the four counts/toggles fit on one
row; Add beatmap moves to its own full-width row. Matches upstream PR
ppy/osu#36200's RoundRow layout."
```

---

## Task 13: Final integration — run the full test suite and visual smoke

**Files:** none modified.

- [x] **Step 1: Run all tournament tests**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```
Expected: all tests PASS. Pay attention to:
- `TestSceneMapPoolScreen` — `TestFewMaps` / `TestJustEnoughMaps` / `TestManyMaps` / `TestJustEnoughMods` / `TestManyMods` / `TestSplitMapPoolByMods` / `TestTiebreakerSetDisplay` / `TestLgaSetScoring` should all still pass (unaffected by this phase's changes).
- New: `TestProtectBanPickOrder`, `TestDisallowPickOpponentProtect`, `TestRemoveProtect`, `TestProtectFields`.
- New: `TestSceneTournamentBeatmapPanel.TestProtectIconRender`.

If any test fails, root-cause and fix; do not skip or `[Ignore]` failing tests.

- [x] **Step 2: Launch the tournament client and visually smoke-test**

Run:
```
dotnet run --project osu.Desktop -- --tournament
```

Manually verify (each step ~30s):
1. **Setup screen** still loads with no console errors.
2. **Round editor** shows the four-column `# of Bans / # of Protects / Allow picking opponent's protects / Best of` row, with the `Add beatmap` button on its own full-width row below. Toggling each widget round-trips into the bracket file (close + reopen the client; values persist).
3. **MapPool screen** with a 15-map LGA-shaped pool: the control panel now lists `Red Protect` / `Blue Protect` after `Blue Pick`. Clicking through the draft:
   - First click highlights `Blue Ban` (auto-set by `setNextMode` to LGA index 0).
   - Each subsequent click auto-advances the mode highlight per LGA order.
   - Protected maps show the corner-badge shield icon tinted by team colour.
   - A banned protected map dims via `borderBox`'s greyscale, but the shield icon stays full-opacity.
   - Right-clicking a picked-and-protected map removes pick first, then protect on second right-click.
4. **Gameplay screen** (during a live spectated match) renders normally — no regressions in unaffected screens.

- [x] **Step 3: Note any visual issues for follow-up**

If anything looks off (e.g. corner-badge clipping wrong, mod icon overlap with protect icon), capture the symptom and decide:
- Tweak inline (minor padding / size adjustment).
- File as known-issue in spec §9 "Open questions" and defer to Phase 1 polish PR.

- [x] **Step 4: Commit any final tweaks (if needed)**

```
git add <files>
git commit -m "polish: <specific tweak>"
```

If no tweaks needed, skip this step. Phase 1 is complete when the test suite is green and the visual smoke matches the spec's intended behavior.
