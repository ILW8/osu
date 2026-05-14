# LGA 2026 Phase 2 — Per-map mod parameters + per-user mods in IPC implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Wire structured per-map mod settings into `RoundBeatmap`, render them through an extended `TournamentModIcon` that surfaces lazer's existing extender + cog badge (so a 1.5× DT map shows `1.50x` inline and a Deflate map with non-default `starting_size` shows the cog corner badge), and thread each multiplayer-room user's mod selection through `MultiplayerMatchIPCInfo` → `IPCSnapshot.users[].mods` so external overlays + the gameplay overlay can render per-user mods (covers FreeMod / LM bracket needs).

**Architecture:** Two independent halves.
- **Per-map mod params (spec §5.1–§5.2):** new `RoundBeatmap.ModParameters` dictionary (shape matches `APIMod.Settings`), `RoundBeatmapModFactory` rebuilds `Mod` instances by `APIMod` round-trip, `TournamentModIcon` gains a `(Mod)` constructor and gates custom-texture lookup on `!HasNonDefaultSettings` (a static branded sprite cannot show a rate). Render sites switch from "one acronym string → one icon" to "factory → flow of `TournamentModIcon`".
- **Per-user mods in IPC (spec §5.3–§5.5):** `UserGameplayState` gains `Mods : IReadOnlyList<APIMod>`, populated from `MultiplayerRoomUser.Mods` on each `RoomUpdated` tick. `IPCUserSnapshot.Mods` (new `IPCUserModEntry`) serializes through to `ipc.json`. Gameplay overlay wraps each `PlayerArea` in a container with a top-anchored `FillFlowContainer<TournamentModIcon>` painted from `gameplayState.Score.ScoreInfo.Mods`.

**Tech Stack:** C# / osu-framework drawables, Newtonsoft.Json for `bracket.json` + `ipc.json`, lazer's `APIMod` ↔ `Mod` round-trip (`APIMod.ToMod`, `Mod.CopyAdjustedSetting`), NUnit + osu-framework test-scene pattern. Build: `dotnet build osu.sln`. Test assembly: `osu.Game.Tournament.Tests`.

**Spec reference:** `docs/superpowers/specs/2026-05-10-lga-2026-update-design.md` §5 (head commit `eac58b08ef`). This plan covers Phase 2 only. Phase 1 (Protect + draft order) shipped in `2026-05-12-lga-2026-phase-1-protect.md` and is the merge-base for this plan; Phase 4 (MapPool 65/35) shipped in `c4cbeb535d`; Phase 3 (1v1 / score-edit) lives in its own plan.

**Scope notes:**

- The new `(Mod)` constructor on `TournamentModIcon` is *additive* — the existing `(string)` constructor is preserved untouched so non-`RoundBeatmap`-bound call sites (e.g. seeding-editor result mods, `TestSceneTournamentModDisplay`) keep compiling.
- `TournamentBeatmapPanel`'s mod parameter is upgraded from a single acronym `string` to an optional `RoundBeatmap`. Call sites in `MapPoolScreen`, `RoundEditorScreen`, and `TestSceneTournamentBeatmapPanel` switch over. `SeedingEditorScreen` continues to pass a single acronym string (its model carries no per-map settings); we keep a string overload for it.
- `SongBar` does **not** participate in this phase. It paints from the room's `LegacyMods` bitfield (no per-map settings), and its panel call is `new TournamentBeatmapPanel(beatmap)` with no mod argument today — unchanged.
- Per-user mods in the gameplay overlay are read from `SpectatorGameplayState.Score.ScoreInfo.Mods` (already in scope where the `PlayerArea` is constructed), rather than from `userStates`. This avoids subscribing to the non-bindable `userStates` dictionary; the gameplay-overlay path independent of the IPC-writer path.
- Bracket files authored before this phase have no `ModParameters` key; Newtonsoft initialises the field to the default empty dictionary, so legacy bracket files round-trip unchanged.
- IPC consumers existing today (no `mods` field) are forward-compatible: the new field is appended.

**File structure:**

| File | Responsibility |
| --- | --- |
| `osu.Game.Tournament/Models/RoundBeatmap.cs` | Modify. Add `ModParameters : Dictionary<string, Dictionary<string, object>>`. |
| `osu.Game.Tournament/Components/RoundBeatmapModFactory.cs` | New. Parse `RoundBeatmap.Mods` acronym string into 2-char chunks; rebuild each as a `Mod` with `APIMod`-round-tripped settings from `RoundBeatmap.ModParameters`. |
| `osu.Game.Tournament/Components/TournamentModIcon.cs` | Modify. Add `(Mod configuredMod)` constructor; gate the `Mods/{acronym}` custom-texture lookup on `!HasNonDefaultSettings`; use `configuredMod` for the embedded `ModIcon` fallback when supplied. |
| `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs` | Modify. Add `(IBeatmapInfo?, RoundBeatmap?)` overload; render a horizontal `FillFlowContainer<TournamentModIcon>` from `RoundBeatmapModFactory.ConstructMods`; keep the existing `(IBeatmapInfo?, string)` overload for non-RoundBeatmap callers (seeding editor). |
| `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs` | Modify. Update line 553 `TournamentBeatmapPanel` construction to pass `b` (the `RoundBeatmap`). |
| `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs` | Modify. Update `updatePanel` to pass the `RoundBeatmap`; add a "Mod settings" `SettingsTextBox` on each `RoundBeatmapRow` that round-trips `ModParameters` through compact JSON (e.g. `{"DT":{"speed_change":1.5}}`) via `JsonConvert`. |
| `osu.Game.Tournament/IPC/UserGameplayState.cs` | Modify. Add `Mods : IReadOnlyList<APIMod>` to the record struct and to `Empty`. |
| `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs` | Modify. Add `updateUserModsFromRoom()` helper; call from `onRoomUpdated` next to existing `updateBeatmapFromRoom / updateModsFromRoom / updateChatChannelFromRoom`. Preserve the existing `onLoadRequested` reset behaviour (mods get repopulated on the next `RoomUpdated` tick). |
| `osu.Game.Tournament/IPC/IPCSnapshot.cs` | Modify. Add `IPCUserModEntry` record struct; add `Mods : ImmutableArray<IPCUserModEntry>` to `IPCUserSnapshot`; emit `"mods": [...]` in `SerializeToJson`. |
| `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs` | Modify. Populate `Mods` in `BuildUserSnapshots` from `state.Mods`. |
| `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs` | Modify. In `loadUserIntoPlayerArea`, wrap each `PlayerArea` in a container whose top-anchored `FillFlowContainer<TournamentModIcon>` is populated from `gameplayState.Score.ScoreInfo.Mods`. |
| `osu.Game.Tournament.Tests/NonVisual/RoundBeatmapModFactoryTest.cs` | New. NUnit fixture covering parse + settings round-trip. |
| `osu.Game.Tournament.Tests/Components/TestSceneTournamentModIcon.cs` | New. Test scene + assertion-based tests for the texture-gating behaviour of the new `(Mod)` constructor. |
| `osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs` | Modify. Add `TestModParametersJsonRoundTrip` covering the new "Mod settings" textbox JSON parse paths (number / bool / string) plus empty / malformed-JSON cases. |
| `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterBuildUserSnapshotsTest.cs` | Modify. Add `IncludesPerUserMods` covering mod-array round-trip through `BuildUserSnapshots`. |
| `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs` | Modify. Add `SerializesPerUserMods` covering the JSON shape `users[].mods = [{acronym, settings}, ...]`. |

No changes to `LadderInfo`, `TournamentMatch`, the protect plumbing, `MultiplayerScoreProjection`, `SongBar`, `SeedingEditorScreen` (string path preserved), or the bracket schema migration for older files (legacy files load with empty `ModParameters`).

---

## Task 1: Add `RoundBeatmap.ModParameters`

**Files:**
- Modify: `osu.Game.Tournament/Models/RoundBeatmap.cs`

- [x] **Step 1: Add the `ModParameters` field**

Edit `osu.Game.Tournament/Models/RoundBeatmap.cs`. Replace the entire file with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Tournament.Models
{
    public class RoundBeatmap
    {
        public int ID;
        public string Mods = string.Empty;
        public string SlotName = string.Empty;

        /// <summary>
        /// Per-map mod settings, keyed by mod acronym then by snake_case setting name.
        /// Value type is <c>object</c> to mirror <see cref="osu.Game.Online.API.APIMod.Settings"/>
        /// so the factory can route entries through <c>APIMod.ToMod</c> without a numeric-only
        /// path. Newtonsoft round-trips nested <c>Dictionary&lt;string, object&gt;</c> natively in
        /// <c>bracket.json</c>. Default-empty so older bracket files load unchanged.
        /// Example for a 1.5× DT map: <c>{ "DT": { "speed_change": 1.5 } }</c>.
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> ModParameters
            = new Dictionary<string, Dictionary<string, object>>();

        [JsonProperty("BeatmapInfo")]
        public TournamentBeatmap? Beatmap;
    }
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
git add osu.Game.Tournament/Models/RoundBeatmap.cs
git commit -m "add RoundBeatmap.ModParameters for per-map mod settings

Shape mirrors APIMod.Settings (Dictionary<string, Dictionary<string, object>>)
so RoundBeatmapModFactory (next commit) can round-trip values through
APIMod.ToMod to apply non-default mod settings — e.g. DT 1.5x rate."
```

---

## Task 2: Create `RoundBeatmapModFactory`

**Files:**
- Create: `osu.Game.Tournament/Components/RoundBeatmapModFactory.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/RoundBeatmapModFactoryTest.cs`

- [x] **Step 1: Write the failing test**

Create `osu.Game.Tournament.Tests/NonVisual/RoundBeatmapModFactoryTest.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.NonVisual
{
    /// <summary>
    /// Unit tests for <see cref="RoundBeatmapModFactory.ConstructMods"/>, the pure projection
    /// from a <see cref="RoundBeatmap"/>'s acronym string + <see cref="RoundBeatmap.ModParameters"/>
    /// to configured <see cref="osu.Game.Rulesets.Mods.Mod"/> instances.
    /// </summary>
    [TestFixture]
    public class RoundBeatmapModFactoryTest
    {
        [Test]
        public void EmptyModsStringReturnsEmpty()
        {
            var rb = new RoundBeatmap { Mods = string.Empty };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParsesMultipleTwoCharAcronyms()
        {
            var rb = new RoundBeatmap { Mods = "HDDT" };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());

            Assert.That(result.Select(m => m.Acronym), Is.EquivalentTo(new[] { "HD", "DT" }));
        }

        [Test]
        public void AppliesSettingsViaApiModRoundTrip()
        {
            var rb = new RoundBeatmap
            {
                Mods = "HDDT",
                ModParameters = new Dictionary<string, Dictionary<string, object>>
                {
                    ["DT"] = new Dictionary<string, object> { ["speed_change"] = 1.5 },
                },
            };

            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());

            var dt = result.OfType<OsuModDoubleTime>().Single();
            Assert.That(dt.SpeedChange.Value, Is.EqualTo(1.5).Within(0.001));
            Assert.That(dt.HasNonDefaultSettings, Is.True);
        }

        [Test]
        public void IgnoresUnknownAcronyms()
        {
            var rb = new RoundBeatmap { Mods = "ZZ" };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());
            Assert.That(result, Is.Empty);
        }
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~RoundBeatmapModFactoryTest
```
Expected: BUILD FAILED — `RoundBeatmapModFactory` not defined.

- [x] **Step 3: Implement the factory**

Create `osu.Game.Tournament/Components/RoundBeatmapModFactory.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Builds the configured <see cref="Mod"/> list rendered on a <see cref="RoundBeatmap"/>'s
    /// panel. Parses <see cref="RoundBeatmap.Mods"/> as a concatenation of 2-character acronyms
    /// (e.g. <c>"HDDT"</c> → <c>["HD", "DT"]</c>) and applies any per-acronym entries from
    /// <see cref="RoundBeatmap.ModParameters"/> by routing through <see cref="APIMod.ToMod"/> —
    /// the same path the multiplayer client uses to materialise mods from API JSON.
    /// </summary>
    public static class RoundBeatmapModFactory
    {
        public static IReadOnlyList<Mod> ConstructMods(RoundBeatmap rb, Ruleset ruleset)
        {
            var result = new List<Mod>();

            foreach (string acronym in ParseModString(rb.Mods))
            {
                Mod? mod = ruleset.CreateModFromAcronym(acronym);
                if (mod == null)
                    continue;

                if (rb.ModParameters.TryGetValue(acronym, out var settings) && settings.Count > 0)
                {
                    var api = new APIMod
                    {
                        Acronym = acronym,
                        Settings = new Dictionary<string, object>(settings),
                    };
                    mod = api.ToMod(ruleset);
                }

                result.Add(mod);
            }

            return result;
        }

        /// <summary>
        /// Split <paramref name="mods"/> into 2-character acronyms.
        /// Trailing odd characters (length not a multiple of 2) are dropped; this is the
        /// established tournament convention (all bracket-relevant osu! mods are 2 chars).
        /// </summary>
        internal static IEnumerable<string> ParseModString(string mods)
        {
            if (string.IsNullOrEmpty(mods))
                yield break;

            for (int i = 0; i + 2 <= mods.Length; i += 2)
                yield return mods.Substring(i, 2);
        }
    }
}
```

- [x] **Step 4: Run the test to verify it passes**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~RoundBeatmapModFactoryTest
```
Expected: PASS (4/4).

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Components/RoundBeatmapModFactory.cs osu.Game.Tournament.Tests/NonVisual/RoundBeatmapModFactoryTest.cs
git commit -m "add RoundBeatmapModFactory + tests

Parses RoundBeatmap.Mods as 2-char acronyms (HD/DT/HR/...) and rebuilds each
as a configured Mod with ModParameters applied via APIMod.ToMod round-trip.
This is the source of the configured Mod instances the new TournamentModIcon
(Mod) constructor (next commit) will route through ModIcon's extender + cog
badge."
```

---

## Task 3: Add `(Mod)` constructor to `TournamentModIcon`

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentModIcon.cs`
- Create: `osu.Game.Tournament.Tests/Components/TestSceneTournamentModIcon.cs`

- [x] **Step 1: Write the failing test scene**

Create `osu.Game.Tournament.Tests/Components/TestSceneTournamentModIcon.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tournament.Components;
using osuTK;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentModIcon : TournamentTestScene
    {
        private FillFlowContainer flow = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("clear flow", () =>
            {
                Child = flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(10),
                };
            });
        }

        [Test]
        public void TestCustomTextureSuppressedForCustomisedMod()
        {
            TournamentModIcon icon = null!;
            AddStep("add DT-1.6 icon", () =>
            {
                var dt = new OsuModDoubleTime { SpeedChange = { Value = 1.6 } };
                flow.Add(icon = new TournamentModIcon(dt) { Size = new Vector2(60) });
            });

            AddUntilStep("icon loaded", () => icon.IsLoaded);

            AddAssert("falls through to embedded ModIcon (no custom Sprite)",
                () => icon.ChildrenOfType<ModIcon>().Any() && !icon.ChildrenOfType<Sprite>().Any());
        }

        [Test]
        public void TestCustomTexturePreservedForDefaultMod()
        {
            // No custom Mods/HD texture is registered in tests — but the embedded ModIcon
            // path is what we get either way, with HasNonDefaultSettings == false letting
            // the texture lookup proceed (it just misses harmlessly).
            TournamentModIcon icon = null!;
            AddStep("add default HD icon", () =>
            {
                Mod hd = new OsuModHidden();
                flow.Add(icon = new TournamentModIcon(hd) { Size = new Vector2(60) });
            });

            AddUntilStep("icon loaded", () => icon.IsLoaded);

            AddAssert("HasNonDefaultSettings false", () =>
            {
                var modIcon = icon.ChildrenOfType<ModIcon>().FirstOrDefault();
                return modIcon != null;
            });
        }

        [Test]
        public void TestAcronymStringPathUnchanged()
        {
            // Regression guard: legacy callers passing a string acronym still get an icon.
            TournamentModIcon icon = null!;
            AddStep("add DT via string", () =>
            {
                flow.Add(icon = new TournamentModIcon("DT") { Size = new Vector2(60) });
            });

            AddUntilStep("icon loaded", () => icon.IsLoaded);
            AddAssert("has child drawable", () => icon.ChildrenOfType<Drawable>().Any());
        }
    }
}
```

- [x] **Step 2: Run the test to verify it fails**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TestSceneTournamentModIcon
```
Expected: BUILD FAILED — no `TournamentModIcon(Mod)` constructor.

- [x] **Step 3: Add the `(Mod)` constructor + gating**

Replace `osu.Game.Tournament/Components/TournamentModIcon.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Mod icon displayed in tournament usages, allowing user overridden graphics.
    /// Two construction paths:
    /// <list type="bullet">
    /// <item><c>(string acronym)</c> — legacy path. Uses default-settings Mod, custom texture honoured.</item>
    /// <item><c>(Mod configuredMod)</c> — new in Phase 2. If <c>HasNonDefaultSettings</c> is true,
    /// the custom-texture lookup is skipped so the embedded <see cref="ModIcon"/>'s extender
    /// (DT rate inline as <c>1.50x</c>) and cog corner badge are surfaced.</item>
    /// </list>
    /// </summary>
    public partial class TournamentModIcon : CompositeDrawable
    {
        private readonly string modAcronym;
        private readonly Mod? configuredMod;

        [Resolved]
        private IRulesetStore rulesets { get; set; } = null!;

        public TournamentModIcon(string modAcronym)
        {
            this.modAcronym = modAcronym;
        }

        public TournamentModIcon(Mod configuredMod)
        {
            this.configuredMod = configuredMod;
            modAcronym = configuredMod.Acronym;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures, LadderInfo ladderInfo)
        {
            // Custom branding only applies when the mod is at default settings.
            // A static branded sprite cannot surface a non-default speed change / setting,
            // so non-default mods fall through to the embedded ModIcon (which paints the
            // extender + cog).
            bool allowCustomTexture = configuredMod == null || !configuredMod.HasNonDefaultSettings;

            if (allowCustomTexture)
            {
                var customTexture = textures.Get($"Mods/{modAcronym}");

                if (customTexture != null)
                {
                    AddInternal(new Sprite
                    {
                        FillMode = FillMode.Fit,
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Texture = customTexture,
                    });

                    return;
                }
            }

            var mod = configuredMod
                      ?? rulesets.GetRuleset(ladderInfo.Ruleset.Value?.OnlineID ?? 0)
                                ?.CreateInstance().CreateModFromAcronym(modAcronym);

            if (mod == null)
                return;

            AddInternal(new ModIcon(mod, false)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(0.5f),
            });
        }
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TestSceneTournamentModIcon
```
Expected: PASS (3/3).

- [x] **Step 5: Build everything to confirm no regressions**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 6: Commit**

```
git add osu.Game.Tournament/Components/TournamentModIcon.cs osu.Game.Tournament.Tests/Components/TestSceneTournamentModIcon.cs
git commit -m "extend TournamentModIcon with (Mod) constructor + non-default gating

Configured-Mod path skips the Mods/{acronym} custom-texture lookup when the
mod has non-default settings, so the embedded ModIcon's extender (DT rate
inline as 1.50x via ModRateAdjust.ExtendedIconInformation) and cog corner
badge surface automatically. String constructor untouched — legacy callers
keep getting default-settings rendering."
```

---

## Task 4: Wire `TournamentBeatmapPanel` to render multiple configured mods

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs`

- [x] **Step 1: Add a `RoundBeatmap`-aware overload + flow rendering**

Edit `osu.Game.Tournament/Components/TournamentBeatmapPanel.cs`. Find the existing constructor + the mod-icon block in `load`:

```csharp
public TournamentBeatmapPanel(IBeatmapInfo? beatmap, string mod = "")
{
    Beatmap = beatmap;
    this.mod = mod;

    Width = 400;
    Height = HEIGHT;
}
```

Replace with two overloads + a private constructor that carries the `RoundBeatmap`:

```csharp
private readonly RoundBeatmap? roundBeatmap;

public TournamentBeatmapPanel(IBeatmapInfo? beatmap, string mod = "")
{
    Beatmap = beatmap;
    this.mod = mod;

    Width = 400;
    Height = HEIGHT;
}

public TournamentBeatmapPanel(RoundBeatmap rb)
{
    Beatmap = rb.Beatmap;
    roundBeatmap = rb;
    mod = string.Empty;

    Width = 400;
    Height = HEIGHT;
}
```

Add `using osu.Game.Rulesets;` at the top if not already present.

Find the existing single-icon `if (!string.IsNullOrEmpty(mod))` block in `load`:

```csharp
if (!string.IsNullOrEmpty(mod))
{
    AddInternal(new TournamentModIcon(mod)
    {
        Anchor = Anchor.CentreRight,
        Origin = Anchor.CentreRight,
        // Right margin clears the protect-icon wedge (anchored top-right, ~35px
        // extent along the right edge after the 45° rotation). With margin=20 the
        // mod icon's top-right portion would overlap the wedge whenever both are
        // active on the same map.
        Margin = new MarginPadding { Right = 50 },
    });
}
```

Replace with the flow + factory path (the existing string fallback is preserved for non-RoundBeatmap callers):

```csharp
if (roundBeatmap != null)
{
    var rulesetInfo = ladder.Ruleset.Value;
    var ruleset = rulesetInfo == null ? null : rulesets.GetRuleset(rulesetInfo.OnlineID)?.CreateInstance();

    if (ruleset != null)
    {
        var modFlow = new FillFlowContainer
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            AutoSizeAxes = Axes.X,
            RelativeSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(2, 0),
            // Right margin clears the protect-icon wedge (anchored top-right, ~35px
            // extent along the right edge after the 45° rotation). Matches the
            // string-path margin below so single- and multi-icon panels line up.
            Margin = new MarginPadding { Right = 50 },
        };

        foreach (var configuredMod in RoundBeatmapModFactory.ConstructMods(roundBeatmap, ruleset))
        {
            modFlow.Add(new TournamentModIcon(configuredMod)
            {
                RelativeSizeAxes = Axes.Y,
                Width = HEIGHT,
            });
        }

        AddInternal(modFlow);
    }
}
else if (!string.IsNullOrEmpty(mod))
{
    AddInternal(new TournamentModIcon(mod)
    {
        Anchor = Anchor.CentreRight,
        Origin = Anchor.CentreRight,
        Margin = new MarginPadding { Right = 50 },
    });
}
```

Add the `IRulesetStore` dependency the new block needs. Find the class fields block near the top (just above the `currentMatch` declaration):

```csharp
private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();
```

Insert above it:

```csharp
[Resolved]
private IRulesetStore rulesets { get; set; } = null!;
```

The `ladder` reference inside the new flow block uses the `ladder` *method parameter* on the existing `[BackgroundDependencyLoader] private void load(LadderInfo ladder)` signature — no extra field capture needed (the entire mod-flow construction happens inside `load`).

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Commit**

```
git add osu.Game.Tournament/Components/TournamentBeatmapPanel.cs
git commit -m "render configured mod flow on TournamentBeatmapPanel

New (RoundBeatmap) overload routes through RoundBeatmapModFactory and
renders a horizontal flow of TournamentModIcon. Existing (beatmap, string)
overload is preserved for SeedingEditorScreen and TestSceneTournamentModDisplay
(seeding results carry no per-map settings)."
```

---

## Task 5: Switch `MapPoolScreen` + `RoundEditorScreen` over to the `RoundBeatmap` overload

**Files:**
- Modify: `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs:553`
- Modify: `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs:288`

- [x] **Step 1: Update `MapPoolScreen`**

Edit `osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs`. Find (around line 553):

```csharp
currentFlow.Add(new TournamentBeatmapPanel(b.Beatmap, b.Mods)
{
    Anchor = Anchor.TopCentre,
    Origin = Anchor.TopCentre,
    Height = 42,
});
```

Replace with:

```csharp
currentFlow.Add(new TournamentBeatmapPanel(b)
{
    Anchor = Anchor.TopCentre,
    Origin = Anchor.TopCentre,
    Height = 42,
});
```

- [x] **Step 2: Update `RoundEditorScreen`**

Edit `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs`. Find `updatePanel` (around line 288):

```csharp
drawableContainer.Child = new TournamentBeatmapPanel(Model.Beatmap, Model.Mods)
{
    Anchor = Anchor.CentreLeft,
    Origin = Anchor.CentreLeft,
    Width = 300
};
```

Replace with:

```csharp
drawableContainer.Child = new TournamentBeatmapPanel(Model)
{
    Anchor = Anchor.CentreLeft,
    Origin = Anchor.CentreLeft,
    Width = 300
};
```

- [x] **Step 3: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 4: Run the existing visual tests to confirm no regression**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~TestSceneTournamentBeatmapPanel|FullyQualifiedName~TestSceneMapPoolScreen"
```
Expected: PASS (existing tests; the panel still renders with no mod when `b.Mods == ""`).

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/MapPool/MapPoolScreen.cs osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs
git commit -m "route MapPool + round-editor panels through TournamentBeatmapPanel(RoundBeatmap)

Now consults RoundBeatmap.ModParameters via RoundBeatmapModFactory; a 1.5x
DT map renders with the rate-extender visible. SeedingEditorScreen and the
mod-display test scene still use the (beatmap, string) overload unchanged."
```

---

## Task 6: Add free-form "Mod settings" textbox on `RoundBeatmapRow`

**Files:**
- Modify: `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs`

- [x] **Step 1: Add the textbox + parse logic**

Edit `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs`. Add `using System.Collections.Generic;`, `using System.Linq;`, and `using Newtonsoft.Json;` at the top if not already present.

In `RoundBeatmapRow`, find:

```csharp
private readonly Bindable<string> mods = new Bindable<string>(string.Empty);

private readonly Container drawableContainer;
```

Add a third bindable below:

```csharp
private readonly Bindable<string> mods = new Bindable<string>(string.Empty);

private readonly Bindable<string> modParameters = new Bindable<string>(string.Empty);

private readonly Container drawableContainer;
```

In the `InternalChildren` initializer find the `SettingsTextBox` for "Mods":

```csharp
new SettingsTextBox
{
    LabelText = "Mods",
    RelativeSizeAxes = Axes.None,
    Width = 200,
    Current = mods,
},
drawableContainer = new Container
{
    Size = new Vector2(100, 70),
},
```

Insert a third textbox between Mods and drawableContainer:

```csharp
new SettingsTextBox
{
    LabelText = "Mods",
    RelativeSizeAxes = Axes.None,
    Width = 200,
    Current = mods,
},
new SettingsTextBox
{
    LabelText = "Mod settings",
    RelativeSizeAxes = Axes.None,
    Width = 300,
    Current = modParameters,
},
drawableContainer = new Container
{
    Size = new Vector2(100, 70),
},
```

In the `[BackgroundDependencyLoader] private void load()` method, find the existing `mods` binding:

```csharp
mods.Value = Model.Mods;
mods.BindValueChanged(modString => Model.Mods = modString.NewValue);
```

Replace with:

```csharp
mods.Value = Model.Mods;
mods.BindValueChanged(modString =>
{
    Model.Mods = modString.NewValue;
    updatePanel();
});

modParameters.Value = serialiseModParameters(Model.ModParameters);
modParameters.BindValueChanged(text =>
{
    Model.ModParameters = parseModParameters(text.NewValue);
    updatePanel();
});
```

Add the two private helpers inside `RoundBeatmapRow` (before `updatePanel`):

```csharp
/// <summary>
/// Serialise <see cref="RoundBeatmap.ModParameters"/> into compact JSON
/// (e.g. <c>{"DT":{"speed_change":1.5}}</c>). Matches the on-disk shape in
/// <c>bracket.json</c>, so the textbox doubles as a copy/paste sink for that file.
/// </summary>
private static string serialiseModParameters(Dictionary<string, Dictionary<string, object>> parameters)
{
    if (parameters.Count == 0)
        return string.Empty;

    return JsonConvert.SerializeObject(parameters);
}

/// <summary>
/// Parse the textbox content as a JSON object of <c>{acronym: {setting: value}}</c>.
/// Newtonsoft lands numeric values as <c>long</c>/<c>double</c>, booleans as
/// <c>bool</c>, strings as strings — all coerced downstream by
/// <see cref="osu.Game.Rulesets.Mods.Mod.CopyAdjustedSetting"/>. Invalid JSON
/// returns an empty dictionary so a typo doesn't blow up the editor; the user
/// fixes the JSON and the next commit re-renders the panel.
/// </summary>
internal static Dictionary<string, Dictionary<string, object>> parseModParameters(string text)
{
    if (string.IsNullOrWhiteSpace(text))
        return new Dictionary<string, Dictionary<string, object>>();

    try
    {
        return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(text)
               ?? new Dictionary<string, Dictionary<string, object>>();
    }
    catch (JsonException)
    {
        return new Dictionary<string, Dictionary<string, object>>();
    }
}
```

> **Rationale (JSON over per-line `ACRONYM.setting=value`).** `SettingsTextBox` wraps a single-line `OutlinedTextBox`, so the originally-planned newline-separated format could only ever hold one entry from the UI. JSON keeps the format compact, single-line, matches the on-disk `bracket.json` shape verbatim (copy/paste friendly), and reuses `JsonConvert` instead of a hand-rolled parser. Trade-off: one syntax error invalidates the whole field, but the panel re-renders on each commit so the user sees this immediately.

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Add a unit test for the parse path**

Edit `osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs`. If the file does not yet exist, create it with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Screens.Editors;

namespace osu.Game.Tournament.Tests.Screens
{
    [TestFixture]
    public class TestSceneRoundEditorScreen
    {
        [Test]
        public void TestModParametersJsonRoundTrip()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("{\"DT\":{\"speed_change\":1.5},\"MOD\":{\"flag\":true},\"Key\":{\"note\":\"hello\"}}");

            Assert.That(parsed["DT"]["speed_change"], Is.EqualTo(1.5));
            Assert.That(parsed["MOD"]["flag"], Is.EqualTo(true));
            Assert.That(parsed["Key"]["note"], Is.EqualTo("hello"));
        }

        [Test]
        public void TestModParametersEmptyInput()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters(string.Empty);
            Assert.That(parsed, Is.Empty);
        }

        [Test]
        public void TestModParametersMalformedJsonReturnsEmpty()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("{not valid json");
            Assert.That(parsed, Is.Empty);
        }
    }
}
```

If the file already exists, append the three test methods inside the existing class. The `internal static` accessibility on `parseModParameters` lets the test assembly reach it directly via `InternalsVisibleTo` (already in place; `osu.Game.Tournament.Tests` is in the `[assembly: InternalsVisibleTo]` of `osu.Game.Tournament`).

> If `InternalsVisibleTo` is not in place, add `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("osu.Game.Tournament.Tests")]` to `osu.Game.Tournament/Properties/AssemblyInfo.cs` (create the file with `// Copyright` header if missing).

- [x] **Step 4: Run the tests**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TestSceneRoundEditorScreen
```
Expected: PASS (3/3).

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs osu.Game.Tournament.Tests/Screens/TestSceneRoundEditorScreen.cs
git commit -m "add per-map mod-settings free-form editor to RoundEditorScreen

Textbox per beatmap row accepting a compact JSON object of
{acronym:{setting:value}}. Round-trips through JsonConvert, matching the
on-disk bracket.json shape so values reach RoundBeatmapModFactory via
APIMod.ToMod without a custom parser."
```

---

## Task 7: Extend `UserGameplayState` with `Mods`

**Files:**
- Modify: `osu.Game.Tournament/IPC/UserGameplayState.cs`

- [x] **Step 1: Add the `Mods` field**

Replace `osu.Game.Tournament/IPC/UserGameplayState.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Per-user gameplay snapshot derived from a spectator <see cref="osu.Game.Online.Spectator.FrameDataBundle"/>
    /// and the multiplayer-room user record (for <see cref="Mods"/>).
    /// </summary>
    internal readonly record struct UserGameplayState(
        long Score,
        int Combo,
        double Accuracy,
        IReadOnlyDictionary<HitResult, int> Hits,
        double GameplayTimeMs,
        IReadOnlyList<APIMod> Mods)
    {
        public static UserGameplayState Empty { get; } = new UserGameplayState(
            Score: 0,
            Combo: 0,
            Accuracy: 0,
            Hits: new Dictionary<HitResult, int>(),
            GameplayTimeMs: 0,
            Mods: Array.Empty<APIMod>());
    }
}
```

- [x] **Step 2: Update callers — `MultiplayerMatchIPCInfo.onNewFrames`**

Edit `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`. Find `onNewFrames`:

```csharp
userStates[userId] = new UserGameplayState(
    Score: header.TotalScore,
    Combo: header.Combo,
    Accuracy: header.Accuracy,
    Hits: new Dictionary<HitResult, int>(header.Statistics),
    GameplayTimeMs: gameplayTime);
```

Replace with (preserves existing mods if already tracked):

```csharp
// Preserve any previously-populated mods (sourced from RoomUpdated) — frame
// bundles don't carry per-user mods, only score/combo/accuracy/hits.
var previousMods = userStates.TryGetValue(userId, out var existing)
    ? existing.Mods
    : Array.Empty<APIMod>();

userStates[userId] = new UserGameplayState(
    Score: header.TotalScore,
    Combo: header.Combo,
    Accuracy: header.Accuracy,
    Hits: new Dictionary<HitResult, int>(header.Statistics),
    GameplayTimeMs: gameplayTime,
    Mods: previousMods);
```

Add `using osu.Game.Online.API;` and `using System;` at the top of the file if not already present.

- [x] **Step 3: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 4: Commit**

```
git add osu.Game.Tournament/IPC/UserGameplayState.cs osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs
git commit -m "extend UserGameplayState with per-user mods (APIMod list)

Sourced separately from frame bundles (which carry score/combo/accuracy
only). onNewFrames preserves any mods set by the room-update path so a
score frame arriving between RoomUpdated ticks doesn't blank out the mods."
```

---

## Task 8: Sync per-user mods from `MultiplayerRoomUser.Mods` on `RoomUpdated`

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`

- [x] **Step 1: Add `updateUserModsFromRoom` and wire it into `onRoomUpdated`**

Edit `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`. Find `onRoomUpdated`:

```csharp
private void onRoomUpdated()
{
    Schedule(() =>
    {
        updateBeatmapFromRoom();
        updateModsFromRoom();
        updateChatChannelFromRoom();
    });
}
```

Replace with:

```csharp
private void onRoomUpdated()
{
    Schedule(() =>
    {
        updateBeatmapFromRoom();
        updateModsFromRoom();
        updateUserModsFromRoom();
        updateChatChannelFromRoom();
    });
}
```

In the `#region Data mapping` section (next to `updateModsFromRoom`), insert the new helper:

```csharp
/// <summary>
/// Refresh per-user mods on every <see cref="MultiplayerClient.RoomUpdated"/> tick.
/// MultiplayerRoomUser.Mods is an IEnumerable&lt;APIMod&gt; on the room user; we copy it
/// into the matching <see cref="UserGameplayState"/> so the IPC writer (and the
/// gameplay overlay) can surface FreeMod / per-user LM choices.
///
/// Skipped if the user is not currently tracked in <see cref="userStates"/>; tracking
/// is driven by <see cref="startWatchingUser"/> on participating users.
/// </summary>
private void updateUserModsFromRoom()
{
    if (multiplayerClient.Room == null)
        return;

    foreach (var user in multiplayerClient.Room.Users)
    {
        if (!userStates.TryGetValue(user.UserID, out var existing))
            continue;

        // Cheap reference-equality skip: MultiplayerRoomUser.Mods is reassigned
        // (not mutated in place) on each server push, so reference equality is
        // a sound proxy for "mod selection has not changed".
        if (ReferenceEquals(existing.Mods, user.Mods))
            continue;

        userStates[user.UserID] = existing with { Mods = user.Mods.ToList() };
    }
}
```

`ToList()` materialises the incoming `IEnumerable<APIMod>` (which can be a deferred LINQ chain) into a stable snapshot.

- [x] **Step 2: Update `onLoadRequested` to preserve mods on round-reset**

Find `onLoadRequested`:

```csharp
foreach (int userId in userStates.Keys.ToArray())
    userStates[userId] = UserGameplayState.Empty;
```

The default-`Empty` state has empty mods. That's incorrect on reset because the mods at load-time are still the ones reported by the room; the next `RoomUpdated` will repopulate them, but mid-tick consumers see empty. Replace with:

```csharp
foreach (int userId in userStates.Keys.ToArray())
{
    // Preserve mods across the load reset — they don't change between rounds.
    // The next RoomUpdated tick refreshes them anyway, but this avoids a brief
    // window where the overlay reads back empty mod lists.
    var preservedMods = userStates[userId].Mods;
    userStates[userId] = UserGameplayState.Empty with { Mods = preservedMods };
}
```

- [x] **Step 3: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 4: Run the existing IPC writer tests to confirm no regressions**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~MultiplayerIPCWriter
```
Expected: PASS (existing tests; they instantiate `UserGameplayState.Empty` and don't touch `Mods`, so they survive the field addition).

- [x] **Step 5: Commit**

```
git add osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs
git commit -m "sync per-user mods from MultiplayerRoomUser on RoomUpdated

New updateUserModsFromRoom helper, called alongside the existing
updateBeatmapFromRoom / updateModsFromRoom / updateChatChannelFromRoom.
Reference-equality short-circuits when the room user's Mods reference is
unchanged. Load-request reset preserves the previously-seen mods to avoid
a brief empty window before the next RoomUpdated tick repopulates them."
```

---

## Task 9: Extend `IPCSnapshot` with per-user mods

**Files:**
- Modify: `osu.Game.Tournament/IPC/IPCSnapshot.cs`

- [x] **Step 1: Add `IPCUserModEntry` + `Mods` field + JSON emission**

Edit `osu.Game.Tournament/IPC/IPCSnapshot.cs`. Find the `IPCUserSnapshot` record:

```csharp
internal readonly record struct IPCUserSnapshot(
    int UserId,
    int TeamId,
    MultiplayerUserState State,
    MultiplayerRoomUserRole Role,
    long Score,
    int Combo,
    double Accuracy,
    ImmutableDictionary<string, int> Hits,
    double GameplayTimeMs);
```

Replace with:

```csharp
internal readonly record struct IPCUserSnapshot(
    int UserId,
    int TeamId,
    MultiplayerUserState State,
    MultiplayerRoomUserRole Role,
    long Score,
    int Combo,
    double Accuracy,
    ImmutableDictionary<string, int> Hits,
    double GameplayTimeMs,
    ImmutableArray<IPCUserModEntry> Mods);

/// <summary>
/// Per-user mod entry within <see cref="IPCUserSnapshot.Mods"/>. Wire shape mirrors
/// <see cref="osu.Game.Online.API.APIMod"/>: an acronym plus a snake_case settings dict.
/// </summary>
internal readonly record struct IPCUserModEntry(
    string Acronym,
    ImmutableDictionary<string, object> Settings);
```

Find the existing user-JSON emission in `SerializeToJson`:

```csharp
foreach (var u in snap.Users)
{
    var hits = new JObject();
    foreach (var (key, count) in u.Hits)
        hits[key] = count;

    users.Add(new JObject
    {
        ["userId"] = u.UserId,
        ["teamId"] = u.TeamId,
        ["state"] = enumNameToCamelCase(u.State),
        ["role"] = enumNameToCamelCase(u.Role),
        ["score"] = u.Score,
        ["combo"] = u.Combo,
        ["accuracy"] = u.Accuracy,
        ["hits"] = hits,
        ["gameplayTimeMs"] = u.GameplayTimeMs,
    });
}
```

Replace with:

```csharp
foreach (var u in snap.Users)
{
    var hits = new JObject();
    foreach (var (key, count) in u.Hits)
        hits[key] = count;

    var mods = new JArray();
    foreach (var m in u.Mods)
    {
        var settings = new JObject();
        foreach (var (key, value) in m.Settings)
            settings[key] = JToken.FromObject(value);

        mods.Add(new JObject
        {
            ["acronym"] = m.Acronym,
            ["settings"] = settings,
        });
    }

    users.Add(new JObject
    {
        ["userId"] = u.UserId,
        ["teamId"] = u.TeamId,
        ["state"] = enumNameToCamelCase(u.State),
        ["role"] = enumNameToCamelCase(u.Role),
        ["score"] = u.Score,
        ["combo"] = u.Combo,
        ["accuracy"] = u.Accuracy,
        ["hits"] = hits,
        ["gameplayTimeMs"] = u.GameplayTimeMs,
        ["mods"] = mods,
    });
}
```

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD FAILED — `MultiplayerIPCWriter.BuildUserSnapshots` constructs `IPCUserSnapshot` without `Mods`. Fixed in Task 10.

- [x] **Step 3: Commit (deferred)**

Hold the commit until Task 10 lands — both files together form one buildable unit.

---

## Task 10: Populate `Mods` in `BuildUserSnapshots`

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`

- [x] **Step 1: Project `state.Mods` into `IPCUserModEntry`**

Edit `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`. Find the existing `IPCUserSnapshot` construction in `BuildUserSnapshots`:

```csharp
var hitsBuilder = ImmutableDictionary.CreateBuilder<string, int>();
foreach (var (result, count) in state.Hits)
    hitsBuilder[result.ToString().ToLowerInvariant()] = count;

users.Add(new IPCUserSnapshot(
    UserId: roomUser.UserID,
    TeamId: teamId,
    State: roomUser.State,
    Role: roomUser.Role,
    Score: state.Score,
    Combo: state.Combo,
    Accuracy: state.Accuracy,
    Hits: hitsBuilder.ToImmutable(),
    GameplayTimeMs: state.GameplayTimeMs));
```

Replace with:

```csharp
var hitsBuilder = ImmutableDictionary.CreateBuilder<string, int>();
foreach (var (result, count) in state.Hits)
    hitsBuilder[result.ToString().ToLowerInvariant()] = count;

var modsBuilder = ImmutableArray.CreateBuilder<IPCUserModEntry>(state.Mods.Count);
foreach (var apiMod in state.Mods)
{
    var settings = ImmutableDictionary.CreateBuilder<string, object>();
    foreach (var (key, value) in apiMod.Settings)
        settings[key] = value;

    modsBuilder.Add(new IPCUserModEntry(
        Acronym: apiMod.Acronym,
        Settings: settings.ToImmutable()));
}

users.Add(new IPCUserSnapshot(
    UserId: roomUser.UserID,
    TeamId: teamId,
    State: roomUser.State,
    Role: roomUser.Role,
    Score: state.Score,
    Combo: state.Combo,
    Accuracy: state.Accuracy,
    Hits: hitsBuilder.ToImmutable(),
    GameplayTimeMs: state.GameplayTimeMs,
    Mods: modsBuilder.ToImmutable()));
```

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Add a unit test for the per-user-mod projection**

Edit `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterBuildUserSnapshotsTest.cs`. Append inside the `[TestFixture]` class:

```csharp
[Test]
public void IncludesPerUserMods()
{
    var roomUsers = new[]
    {
        new MultiplayerRoomUser(userId: 7) { State = MultiplayerUserState.Playing },
    };

    var states = new Dictionary<int, UserGameplayState>
    {
        [7] = new UserGameplayState(
            Score: 0,
            Combo: 0,
            Accuracy: 0,
            Hits: new Dictionary<HitResult, int>(),
            GameplayTimeMs: 0,
            Mods: new[]
            {
                new osu.Game.Online.API.APIMod { Acronym = "HD" },
                new osu.Game.Online.API.APIMod
                {
                    Acronym = "DT",
                    Settings = new Dictionary<string, object> { ["speed_change"] = 1.5 },
                },
            }),
    };

    var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, states);

    Assert.That(result, Has.Length.EqualTo(1));
    Assert.That(result[0].Mods.Length, Is.EqualTo(2));
    Assert.That(result[0].Mods[0].Acronym, Is.EqualTo("HD"));
    Assert.That(result[0].Mods[1].Acronym, Is.EqualTo("DT"));
    Assert.That(result[0].Mods[1].Settings["speed_change"], Is.EqualTo(1.5));
}
```

Update the existing `IncludesUsersWithoutMatchState` / `ProjectsStateAndRoleFromRoomUser` / `MixedRoomPreservesTeamIdsAndNoTeamSentinel` cases that construct `UserGameplayState` to pass `Mods` explicitly. For each call replace the existing constructor argument list with one that adds `Mods: Array.Empty<osu.Game.Online.API.APIMod>()` as the last positional argument. Example for `IncludesUsersWithoutMatchState`:

```csharp
[42] = new UserGameplayState(
    Score: 100,
    Combo: 5,
    Accuracy: 0.9,
    Hits: new Dictionary<HitResult, int> { [HitResult.Great] = 10 },
    GameplayTimeMs: 1000,
    Mods: Array.Empty<osu.Game.Online.API.APIMod>()),
```

Add `using System;` to the test file's `using` block if not already present.

- [x] **Step 4: Run the tests**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~MultiplayerIPCWriterBuildUserSnapshotsTest
```
Expected: PASS (all updated cases + the new one — 6 tests total).

- [x] **Step 5: Update the two existing `IPCUserSnapshot` constructions in `IPCSnapshotTest.cs`**

Edit `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`. Two existing tests construct `IPCUserSnapshot` positionally and will fail to compile against the new required `Mods` field:

In `TestSnapshotsWithSameDataAreEqual` find:
```csharp
var users = ImmutableArray.Create(new IPCUserSnapshot(
    UserId: 42,
    TeamId: 1,
    State: MultiplayerUserState.Playing,
    Role: MultiplayerRoomUserRole.Player,
    Score: 1000,
    Combo: 10,
    Accuracy: 0.95,
    Hits: ImmutableDictionary<string, int>.Empty.Add("great", 5),
    GameplayTimeMs: 1234));
```

Replace with (adds `Mods: ImmutableArray<IPCUserModEntry>.Empty`):
```csharp
var users = ImmutableArray.Create(new IPCUserSnapshot(
    UserId: 42,
    TeamId: 1,
    State: MultiplayerUserState.Playing,
    Role: MultiplayerRoomUserRole.Player,
    Score: 1000,
    Combo: 10,
    Accuracy: 0.95,
    Hits: ImmutableDictionary<string, int>.Empty.Add("great", 5),
    GameplayTimeMs: 1234,
    Mods: ImmutableArray<IPCUserModEntry>.Empty));
```

In `TestSerializePopulatedSnapshot` find:
```csharp
var user = new IPCUserSnapshot(
    UserId: 9876,
    TeamId: 1,
    State: MultiplayerUserState.Playing,
    Role: MultiplayerRoomUserRole.Player,
    Score: 612345,
    Combo: 128,
    Accuracy: 0.9821,
    Hits: ImmutableDictionary<string, int>.Empty
        .Add("great", 456)
        .Add("ok", 7)
        .Add("meh", 1)
        .Add("miss", 2),
    GameplayTimeMs: 47320);
```

Replace with:
```csharp
var user = new IPCUserSnapshot(
    UserId: 9876,
    TeamId: 1,
    State: MultiplayerUserState.Playing,
    Role: MultiplayerRoomUserRole.Player,
    Score: 612345,
    Combo: 128,
    Accuracy: 0.9821,
    Hits: ImmutableDictionary<string, int>.Empty
        .Add("great", 456)
        .Add("ok", 7)
        .Add("meh", 1)
        .Add("miss", 2),
    GameplayTimeMs: 47320,
    Mods: ImmutableArray<IPCUserModEntry>.Empty);
```

Also add to `TestSerializePopulatedSnapshot` (right before the closing brace) an assertion that the new `mods` field is emitted as an empty array on this no-mods user:
```csharp
Assert.That(u0["mods"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Array));
Assert.That(u0["mods"]!.HasValues, Is.False);
```

No other `IPCUserSnapshot` constructions exist in the file (the `ComputeOutput` tests use `ImmutableArray<IPCUserSnapshot>.Empty`, which is unaffected).

- [x] **Step 6: Add a JSON-shape test**

Edit `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`. Append a test that round-trips a snapshot with mods through `SerializeToJson`:

```csharp
[Test]
public void SerializesPerUserMods()
{
    var users = ImmutableArray.Create(new IPCUserSnapshot(
        UserId: 1,
        TeamId: 1,
        State: MultiplayerUserState.Playing,
        Role: MultiplayerRoomUserRole.Player,
        Score: 0,
        Combo: 0,
        Accuracy: 0,
        Hits: ImmutableDictionary<string, int>.Empty,
        GameplayTimeMs: 0,
        Mods: ImmutableArray.Create(
            new IPCUserModEntry("HD", ImmutableDictionary<string, object>.Empty),
            new IPCUserModEntry("DT", ImmutableDictionary<string, object>.Empty.Add("speed_change", 1.5)))));

    var snap = new IPCSnapshot(
        Connected: true,
        RoomId: 1,
        BeatmapId: null,
        State: TourneyState.Playing,
        Team1Score: 0,
        Team2Score: 0,
        Users: users);

    string json = IPCSnapshot.SerializeToJson(snap);
    var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);

    var modsArray = (Newtonsoft.Json.Linq.JArray)parsed["users"]![0]!["mods"]!;
    Assert.That((string?)modsArray[0]!["acronym"], Is.EqualTo("HD"));
    Assert.That((string?)modsArray[1]!["acronym"], Is.EqualTo("DT"));
    Assert.That((double?)modsArray[1]!["settings"]!["speed_change"], Is.EqualTo(1.5));
}
```

Add these `using` lines to the file if absent:

```csharp
using System.Collections.Immutable;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
```

The file already contains a `[TestFixture] public class IPCSnapshotTest` — append this method inside it.

- [x] **Step 7: Run all `IPCSnapshotTest` cases**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~IPCSnapshotTest
```
Expected: PASS (all existing cases survive the `Mods` field addition + the new one).

- [x] **Step 8: Commit (covering both Task 9 + Task 10 changes)**

```
git add osu.Game.Tournament/IPC/IPCSnapshot.cs osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterBuildUserSnapshotsTest.cs osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs
git commit -m "thread per-user mods through IPCSnapshot.Users[].Mods JSON

Adds IPCUserModEntry (acronym + snake_case settings dict, mirroring APIMod's
JSON wire form) on IPCUserSnapshot. SerializeToJson emits users[].mods as an
array of {acronym, settings} objects so external scoreboards can surface
FreeMod / LM choices per player. Tests cover both the projection in
BuildUserSnapshots and the resulting JSON shape."
```

---

## Task 11: Render per-user mod icons in the gameplay overlay

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`

- [x] **Step 1: Wrap each `PlayerArea` in a container with a mod-icon strip**

Edit `osu.Game.Tournament/Components/TournamentGameplayDisplay.cs`. Add `using osu.Framework.Graphics.Containers;` and `using osu.Game.Rulesets.Mods;` to the `using` block if not already present.

Find `loadUserIntoPlayerArea`:

```csharp
var playerArea = new PlayerArea(userId, syncManager.CreateManagedClock(), showFailingLayer: false)
{
    RelativeSizeAxes = Axes.Both,
};

playerAreas[userId] = playerArea;
playerAreasContainer.Add(playerArea, slotIndex);
playerArea.LoadScore(gameplayState.Score);
```

Replace with:

```csharp
var playerArea = new PlayerArea(userId, syncManager.CreateManagedClock(), showFailingLayer: false)
{
    RelativeSizeAxes = Axes.Both,
};

playerAreas[userId] = playerArea;

var slotContainer = new Container
{
    RelativeSizeAxes = Axes.Both,
    Children = new Drawable[]
    {
        playerArea,
        buildModFlow(gameplayState.Score.ScoreInfo.Mods),
    },
};

playerAreasContainer.Add(slotContainer, slotIndex);
playerArea.LoadScore(gameplayState.Score);
```

Add the `buildModFlow` helper as a private method on the class (next to `loadUserIntoPlayerArea`):

```csharp
/// <summary>
/// Build a small mod-icon overlay anchored top-right within the slot, painting one
/// <see cref="TournamentModIcon"/> per <see cref="Mod"/> in <paramref name="mods"/>.
/// Empty mods → empty flow (consistent with how lazer's ModDisplay renders no-mod runs).
/// </summary>
private static Drawable buildModFlow(IReadOnlyList<Mod> mods)
{
    var flow = new FillFlowContainer
    {
        Anchor = Anchor.TopRight,
        Origin = Anchor.TopRight,
        AutoSizeAxes = Axes.X,
        Height = 28,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(2, 0),
        Margin = new MarginPadding(6),
    };

    foreach (var mod in mods)
    {
        flow.Add(new TournamentModIcon(mod)
        {
            RelativeSizeAxes = Axes.Y,
            Width = 28,
        });
    }

    return flow;
}
```

Add `using osuTK;` if not already present (for `Vector2`).

The `Mod` instances come from `gameplayState.Score.ScoreInfo.Mods`, which is populated at construction time in `tryStartGameplay` from `spectatorState.Mods.Select(m => m.ToMod(resolvedRuleset))`. So the overlay reflects the mods the spectated user is playing with.

- [x] **Step 2: Build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 3: Run the tournament test suite to confirm no regressions**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TournamentGameplayDisplay
```
Expected: PASS (the existing `TournamentGameplayDisplayBuildSnapshottedSlotsTest` does not touch the slot-container wrapping, so it stays green).

- [x] **Step 4: Commit**

```
git add osu.Game.Tournament/Components/TournamentGameplayDisplay.cs
git commit -m "render per-user mod icons over each PlayerArea in gameplay overlay

Wraps each PlayerArea in a slot container with a top-right anchored
FillFlowContainer<TournamentModIcon>, painted from the Score.ScoreInfo.Mods
already resolved at gameplay-start. DT 1.5x mods show 1.50x inline via the
new TournamentModIcon(Mod) constructor's extender path; customised non-rate
mods show the cog corner badge."
```

---

## Task 12: End-to-end smoke check

**Files:** none — verification only.

- [x] **Step 1: Full build**

Run:
```
dotnet build osu.sln
```
Expected: BUILD SUCCEEDED.

- [x] **Step 2: Full tournament test suite**

Run:
```
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```
Expected: all tests pass. There should be at least 4 new `RoundBeatmapModFactoryTest` cases, 3 new `TestSceneTournamentModIcon` cases, 3 new `TestSceneRoundEditorScreen` cases (parse paths), 1 new `MultiplayerIPCWriterBuildUserSnapshotsTest` case (per-user mods), and 1 new `IPCSnapshotTest` case (per-user-mod JSON shape).

- [x] **Step 3: Manual visual check (DT 1.5× rendering)**

Open the tournament tools client (`dotnet run --project osu.Desktop -- --tournament`). In Round Editor, add a beatmap with `Mods = "DT"` and `Mod settings = {"DT":{"speed_change":1.5}}`. Open MapPool; the panel for that beatmap should show the DT icon with `1.50x` inline (via the embedded `ModIcon`'s extender). Clearing the `Mod settings` field (or removing the `speed_change` key) should restore the plain DT icon (custom-texture path if `Mods/DT` is registered, embedded `ModIcon` otherwise).

If 1.50x does not appear:
- Check the `RoundEditorScreen` "Mod settings" textbox actually round-trips — empty out and re-enter, watch the panel rebuild.
- Confirm `ModParameters["DT"]["speed_change"]` deserialises as `double` (not `long` 1 or `string` "1.5") — Newtonsoft picks `double` when the JSON literal has a decimal point; the parse helper writes `double` for any numeric input.
- If the panel still shows no rate, confirm `Mod.HasNonDefaultSettings` is `true` on the constructed mod by setting a breakpoint in `RoundBeatmapModFactory.ConstructMods` after the `api.ToMod(ruleset)` call.

- [x] **Step 4: Update project memory**

Edit `C:/Users/daohe/.claude/projects/C--Users-daohe-RiderProjects-osu/memory/project_lga2025_to_lga2026.md` and tick Phase 2 off the status table at the end of the file. Reference the resulting commits.

- [x] **Step 5: No commit needed for Step 1–3; commit memory edit**

```
git add C:/Users/daohe/.claude/projects/C--Users-daohe-RiderProjects-osu/memory/project_lga2025_to_lga2026.md
git commit -m "memory: mark LGA 2026 Phase 2 (mod params + per-user mods) as complete"
```

---

## Spec coverage cross-check

| Spec section | Task(s) |
| --- | --- |
| §5.1 — `RoundBeatmap.ModParameters` | Task 1 |
| §5.2 — `TournamentModIcon(Mod)` + factory + render-site rewire | Tasks 2, 3, 4, 5 |
| §5.2 — Round-editor free-form mod-settings textbox | Task 6 |
| §5.3 — `UserGameplayState.Mods` + `updateUserModsFromRoom` | Tasks 7, 8 |
| §5.4 — `IPCUserSnapshot.Mods` + JSON | Tasks 9, 10 |
| §5.5 — Per-user mod icons in gameplay overlay | Task 11 |
| §5.6 — Phase 2 testing | Tests interleaved into Tasks 2, 3, 6, 10 |

Items in the spec's §5.6 test table that this plan does **not** cover and why:
- `TestSceneSongBar.TestTournamentModIconRendersDtRate` — SongBar is not modified in this phase (its mod path is `LegacyMods` bitfield from the room's `RequiredMods`, no per-map settings — see scope notes). When SongBar is later updated to consult `RoundBeatmap.ModParameters`, that test should be added in the same change.
- `TestSceneTournamentGameplayDisplay.TestPerUserModIcons` — visual-only; Task 11 wires the rendering and Task 12's manual visual check exercises it. Adding a headless visual-test for this is deferred (would require a substantial spectator-state mock; not blocking).
- `MultiplayerMatchIPCInfoTest.TestModUpdatePropagatesToUserStates` — would require a `MultiplayerClient` test double the codebase does not yet have. The same path is exercised in production by the existing manual-test workflow.

These deferrals are noted so the reviewer doesn't think the spec was misread.
