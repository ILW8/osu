# LGA 2026 update — feature/lazer-tournament-spectator

**Status:** Design draft 2026-05-10
**Owner:** ILW8
**Branch:** `feature/lazer-tournament-spectator`
**Comparison tag:** `2025.524.2-LGA+2025.424.0-week2`

## 1. Motivation

The Lazer Grand Arena 2026 ([wiki](https://github.com/ppy/osu-wiki/blob/master/wiki/Tournaments/LGA/2026/en.md)) starts the qualifier stage on 2026-05-03 and the bracket stage on 2026-05-16. The tournament overlay this year runs *inside* the lazer tournament client and consumes match state directly from the multiplayer room (not stable file-IPC), via the `MultiplayerMatchIPCInfo` path that has already landed on `feature/lazer-tournament-spectator`.

A number of features that existed in the 2025 LGA tag have not yet been ported, and several rule changes in 2026 (Lazer Mod bracket, per-map DT speed multiplier, FreeMod tiebreaker) introduce new requirements. This document specifies the remaining work, grouped into four implementation phases that can ship independently.

## 2. Scope

**In scope (this spec):**

| # | Item |
| --- | --- |
| P1.1 | `ChoiceType.Protect` enum value + `TournamentMatch.Protects` collection |
| P1.2 | `TournamentRound.ProtectCount` + `AllowPickingOpponentProtects`; round editor UI |
| P1.3 | `TournamentProtectIcon` (modern corner-badge from PR ppy/osu#36200) on `TournamentBeatmapPanel` |
| P1.4 | LGA 2026 ban/protect/pick sequencing in `MapPoolScreen.setNextMode()` |
| P1.5 | Protect buttons in MapPoolScreen control panel; updated click rules + right-click removal |
| P2.1 | `RoundBeatmap.ModParameters` (structured per-map mod settings) + Round editor UI |
| P2.2 | Per-map mod parameters rendered in `SongBar` and `TournamentBeatmapPanel` |
| P2.3 | Per-user mods in `MultiplayerMatchIPCInfo` + extension to `UserGameplayState` |
| P2.4 | `IPCUserSnapshot.Mods` JSON field + writer serialization |
| P2.5 | Per-user mods rendered in `TournamentGameplayDisplay` (gameplay overlay) |
| P3.1 | Port `LadderInfo.Use1V1Mode` and 5 call sites verbatim from 2025 LGA tag |
| P3.2 | Match-completion auto-detect at 3 set wins |
| P3.3 | Operator score-edit UI (slot dropdown + red/blue score textboxes) in MapPoolScreen |
| P4.1 | MapPool 65/35 split layout with "Pool" / "Sets" headers |

**Out of scope (this spec):**

- Tactical-timeout UI (deferred — operator-triggered ref tooling, not gameplay-critical for v1).
- Set-winner manual override on `TournamentSetPanel` (cumulative scoring auto-determines set winner; LGA 2025's right-click override was deliberately dropped).
- Ladder editor reset-teams confirmation dialog (deferred).
- New tactical timeout fields in `IPCSnapshot`.
- Backwards compatibility with `Use1V1Mode = false` on this branch — branch is LGA-only, but the bindable still defaults `false` so legacy bracket.json round-trips cleanly.
- Changes to `MatchSet`, `TournamentSetPanel`, `MultiplayerIPCWriter` (file path, atomicity, dirty-check), `TournamentPlayerGrid`, `MultiplayerScoreProjection`. These already match 2026 needs.

**Already done on this branch (no spec needed; listed for reader context):**

- Multiplayer-spectator IPC architecture (`MultiplayerMatchIPCInfo`, `MultiplayerIPCWriter`, `IPCSnapshot`).
- Battle-royale grid (`TournamentPlayerGrid` + `VisibleSlotCount` slider) — designed in `2026-04-24-tournament-spectator-battle-royale-design.md`.
- Set-based cumulative scoring (`MatchSet`, `GameplayScreen.updateState` cumulative branch, `MatchHeader.MatchCumulativeScoreDiffCounter`, `MapPoolScreen.updateSets()`).
- `TournamentSetPanel` display in mappool screen.
- LGA bracket position presets, ladder scale buttons, songbar mod plumbing from IPC, hold-to-disconnect, reconnect, room password & invite support, volume controls.

## 3. LGA 2026 ruleset summary (relevant to overlay)

From the 2026 wiki, distilled to what the overlay must surface or enforce:

- **Format:** 1v1 player-vs-player, BO5 sets first-to-3. Each set is 2 maps with cumulative score deciding the set winner. *Only* if the set tally reaches 2-2 does the 5th set become a 3-map set: 2 regular ABBA picks plus the OG map at the end.
- **OG map:** the pool has exactly 1 OG map; on the tiebreaker set it is played under FreeMod (each player chooses any combination of NM / HD / HR).
- **Ban/protect/pick order (bracket stage):**
  - LS bans → HS bans → LS protects → HS protects → LS bans → HS bans (= 4 bans + 2 protects total, interleaved).
  - Protected maps cannot be banned, and can only be picked by the protector.
  - Picks: ABBA starting with HS — for 5 sets × 2 maps that yields the sequence `A B B A A B B A A B` (10 picks).
- **Mappool (per bracket weekend):** NM 4, HD 3, HR 3, DT 3, LM 1, OG 1 = 15 maps.
- **DT bracket:** per-map speed multiplier (e.g. 1.6×, 1.35× in qualifier); the rate must be visible to operators and viewers.
- **LM (Lazer Mod) bracket:** per-map structured mod settings (e.g. Deflate `Starting Size = 1.6` in qualifier); operator-edited; visible.
- **Seed → team mapping (existing on this branch via room-name parsing, commit `5e2a7cbb00`):** Team1 = High seed = Red, Team2 = Low seed = Blue. Comment this assumption in the new code.

## 4. Phase 1 — Pick/ban order + Protect + ProtectIcon

Adopts the data model and UI design from upstream PR [ppy/osu#36200](https://github.com/ppy/osu/pull/36200) wholesale, then overrides the order logic to match LGA's interleaved sequence.

### 4.1 Model changes

**`osu.Game.Tournament/Models/BeatmapChoice.cs`** — add `Protect` to `ChoiceType`:

```csharp
public enum ChoiceType
{
    Pick,    // 0 — preserved
    Ban,     // 1 — preserved
    Protect, // 2 — new
}
```

Numeric ordering preserved, so existing `bracket.json` files with `Pick=0` / `Ban=1` deserialize unchanged.

**`osu.Game.Tournament/Models/TournamentMatch.cs`** — add a separate observable collection for protects (per PR #36200, kept distinct from `PicksBans` so right-click removal can prefer pick-over-protect):

```csharp
public readonly ObservableCollection<BeatmapChoice> Protects = new ObservableCollection<BeatmapChoice>();
```

Update `Reset()` to also clear `Protects` (and, while we're here, `Sets` and `MapScores` — current `Reset()` misses both, which is a latent bug not strictly in scope here but trivial to fix in passing).

**`osu.Game.Tournament/Models/TournamentRound.cs`** — add round-level draft configuration:

```csharp
public readonly BindableInt ProtectCount = new BindableInt
{
    Default = 0,
    MinValue = 0,
    MaxValue = 3,
};

public readonly BindableBool AllowPickingOpponentProtects = new BindableBool(true);
```

LGA 2026 round defaults at the editor / bracket-import level:
- `BanCount.Value = 2`, `ProtectCount.Value = 1` — for `bracket.json` round-trip and future code that may consult them. **Inert on this LGA fork:** the LGA `setNextMode` (§4.5) reads from hardcoded order arrays, not from these counts. They're kept on the model so the round-editor UI and bracket file format stay consistent with upstream PR #36200's data shape, and so a future "non-LGA" round on this branch could use the upstream-style count-driven `setNextMode` if reintroduced.
- `AllowPickingOpponentProtects.Value = false` (per LGA rule "may only be picked by the protector"). **Active** — used by `addForBeatmap` (§4.6).
- `BestOf.Value = 5` — **active**, used by `PointsToWin` for match-complete auto-detect (§6.2).

Do not change the `BindableInt` defaults globally; the spec sets them when LGA rounds are created.

### 4.2 New component — `TournamentProtectIcon`

Port from PR #36200 verbatim into `osu.Game.Tournament/Components/TournamentProtectIcon.cs`. The implementation is a `Container` with:

- A 45°-rotated `Box` background anchored top-right (the corner-badge wedge).
- A `FontAwesome.Solid.ShieldAlt` `SpriteIcon` overlaid at fractional position `(0.14, -0.14)`.
- A nullable `TeamColour?` setter that triggers re-tint via `TournamentGame.GetTeamColour(...)`.
- `Alpha = 0` when `TeamColour == null` (i.e. unprotected), `Alpha = 1` when set.

This replaces the LGA 2025 implementation (which was a full-sized 80×80 `mod-icon`-sized container). The corner-badge form is more space-efficient and is what upstream is converging on.

`TournamentGame.GetTeamColour` — note PR #36200 changes its return type from `ColourInfo` to `Color4`. We adopt the same change here. No callers on this branch rely on the wider `ColourInfo` form (verified via grep before implementation; spec assumes this still holds).

### 4.3 `TournamentBeatmapPanel` changes

Reintroduce the `borderBox` wrapper (`Container` with `Masking = true`) so the protect icon sits *outside* the masked content but inside the panel's outer bounds. Layout per PR #36200:

```
TournamentBeatmapPanel
├── borderBox (Masking = true; the "card" surface that flashes/dims)
│   ├── black Box
│   ├── NoUnloadBeatmapSetCover
│   └── FillFlowContainer (title + mapper + difficulty)
├── protectIcon (CentreRight, square, Width = Height = panel height) ← NEW
├── flash (Box, Additive)
└── modIcon (CentreRight, Margin.Right = 20) — relocated from current bottom-right placement
```

The flash + dim/colour mutations now apply to `borderBox.Colour / .Alpha / .BorderThickness / .BorderColour` instead of `this`, so the corner-badge protect icon and the mod icon don't fade with the ban gray.

`updateState()` consults *both* collections:

```csharp
var protectedChoice = currentMatch.Value.Protects
    .FirstOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);
protectIcon.TeamColour = protectedChoice?.Team;

var newChoice = currentMatch.Value.PicksBans
    .LastOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);
```

`matchChanged` subscribes to `CollectionChanged` on both `PicksBans` and `Protects`, and unsubscribes from both on the old match.

### 4.4 `MapPoolScreen` — protect buttons

Add two buttons after the existing Red/Blue Pick block:

```csharp
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
```

`setMode()` adds two more colour-update lines for the new buttons (mirrors existing pattern).

### 4.5 `MapPoolScreen.setNextMode()` — LGA order arrays

Replace the count-based order logic from PR #36200 with hardcoded arrays. The PR's logic places protects *before* bans; LGA interleaves. Since this branch is LGA-only, hardcoding is acceptable (this is the explicit guidance — no legacy flag).

```csharp
// LGA 2026 §3.4–§3.5. 6 bans/protects, then up to 10 ABBA picks (5 sets × 2 maps).
// Seed mapping (room-name parser, commit 5e2a7cbb): Team1=Red=High seed, Team2=Blue=Low seed.
private static readonly ChoiceType[] mapOperationOrder =
{
    ChoiceType.Ban, ChoiceType.Ban,         // LS, HS
    ChoiceType.Protect, ChoiceType.Protect, // LS, HS
    ChoiceType.Ban, ChoiceType.Ban,         // LS, HS
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
    ChoiceType.Pick, ChoiceType.Pick,
};

// Ban/Protect: Blue, Red ×3.
// Picks (ABBA starting HS): A B B A A B B A A B = R B B R R B B R R B.
private static readonly TeamColour[] teamColourOrder =
{
    TeamColour.Blue, TeamColour.Red,    // ban
    TeamColour.Blue, TeamColour.Red,    // protect
    TeamColour.Blue, TeamColour.Red,    // ban
    TeamColour.Red,  TeamColour.Blue,
    TeamColour.Blue, TeamColour.Red,
    TeamColour.Red,  TeamColour.Blue,
    TeamColour.Blue, TeamColour.Red,
    TeamColour.Red,  TeamColour.Blue,
};
```

`setNextMode()` becomes:

```csharp
private void setNextMode()
{
    if (CurrentMatch.Value == null)
        return;

    int index = CurrentMatch.Value.PicksBans.Count + CurrentMatch.Value.Protects.Count;

    if (index >= mapOperationOrder.Length)
        return; // draft is over — leave mode at last value

    setMode(teamColourOrder[index], mapOperationOrder[index]);
}
```

The arrays are sized for full draft + full BO5 = 16 entries. If `BestOf` or pool size changes for a non-LGA round on this branch, `setNextMode` no-ops past entry 16; that's acceptable for a branch that ships LGA only.

### 4.6 `MapPoolScreen.addForBeatmap()` — protect-aware adding

Adopt the PR #36200 logic:

```csharp
private void addForBeatmap(int beatmapId)
{
    if (CurrentMatch.Value?.Round.Value == null)
        return;

    if (CurrentMatch.Value.Round.Value.Beatmaps.All(b => b.Beatmap?.OnlineID != beatmapId))
        return;

    var existingProtect = CurrentMatch.Value.Protects
        .FirstOrDefault(p => p.BeatmapID == beatmapId);

    bool alreadyHandled = existingProtect != null
                          || CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId);

    if (alreadyHandled)
    {
        // Map already in some state. The only legal follow-up is a pick *of a protected map* —
        // and that pick may be by either team or only by the protector, depending on
        // AllowPickingOpponentProtects.
        bool allowPick = existingProtect != null;

        if (!CurrentMatch.Value.Round.Value.AllowPickingOpponentProtects.Value)
        {
            if (pickType != ChoiceType.Pick || pickColour != existingProtect?.Team)
                allowPick = false;
        }

        // Already picked after protect → reject.
        if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId
                                                  && p.Type == ChoiceType.Pick))
            allowPick = false;

        if (!allowPick) return;
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

    // Existing auto-progress branch unchanged.
}
```

### 4.7 `MapPoolScreen.OnMouseDown` — right-click removal

Right-click order: try to remove a pick or ban first; if none exists for this map, fall back to removing the protect (matches PR #36200's two-stage right-click).

```csharp
else // right-click
{
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
```

### 4.8 `MapPoolScreen.reset()` — clear both collections

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

### 4.9 `RoundEditorScreen` — protect settings UI

Adopt PR #36200's `RoundRow` layout: shrink `# of Bans` from 0.33f width to 0.24f, add `# of Protects` slider and `Allow picking opponent's protects` checkbox at 0.24f each, keep `Best of` at 0.24f, and float the "Add beatmap" button to a new full-width row.

### 4.10 Phase 1 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneMapPoolScreen.TestProtectBanPickOrder` | LGA interleaved order: simulate 16 clicks, assert collection counts (2 protects after click 4, 4 bans after click 6, then picks). |
| `TestSceneMapPoolScreen.TestDisallowPickOpponentProtect` | With `AllowPickingOpponentProtects=false`, opponent cannot pick a protected map; protector can. |
| `TestSceneMapPoolScreen.TestRemoveProtect` | Right-click on protected-then-picked map first removes pick, second click removes protect. |
| `TestSceneMapPoolScreen.TestPickBanOrder` (existing) | **Update or delete** — current assertions expect simple ABBA. Either rewrite to LGA order or remove. |
| `TestSceneMapPoolScreen.TestBanOrderMultipleBans` (existing) | **Update or delete** — same reason. |
| `TestSceneMapPoolScreen.TestMultipleTeamBans` (existing) | **Update or delete** — same reason. |
| `TestSceneTournamentBeatmapPanel.TestProtectIconRender` (new) | Visual: render panel with protect by Red and by Blue; assert `protectIcon.Alpha == 1` and tint is correct; assert flash/dim affects `borderBox`, not `protectIcon`. |
| `TestSceneRoundEditorScreen.TestProtectFields` (new) | Round editor exposes the two new fields; values round-trip via the bindables. |

## 5. Phase 2 — Per-map mod params + per-user mods in IPC + LM/FreeMod display

This phase extends the existing `TournamentModIcon` to accept a configured `Mod` instance (in addition to the existing acronym-string constructor). `TournamentModIcon` already wraps lazer's `ModIcon` with `ShowExtendedInformation = true` in its fallback path, so the DT/HT rate extender and the cog corner badge for non-default settings light up automatically once we feed it a `Mod` with the right settings. No bespoke chip rendering, no `FormatModParameter` helper, no second icon class — keeps custom-texture branding for parameterless mods (NM/HD/HR) and falls through to the embedded `ModIcon` for any mod with non-default settings. The spec ships per-map mod *storage* + round-editor input, the `TournamentModIcon` constructor extension, plus per-user mod plumbing through IPC.

### 5.1 `RoundBeatmap.ModParameters` — structured per-map mod settings

Extend `RoundBeatmap` with a settings bag aligned to lazer's `APIMod.Settings` shape. The map's `Mods` string ("DT", "LM", "OG", etc.) is the bracket; `ModParameters` describes the *settings* of the mods active on this map.

```csharp
public class RoundBeatmap
{
    public int ID;
    public string Mods = string.Empty;
    public string SlotName = string.Empty;

    // NEW. Map of lazer-mod acronym → setting name → value.
    // Mirrors APIMod.Settings (Dictionary<string, object>) so values are interoperable
    // with lazer's mod-construction path: see Mod.CopyAdjustedSetting / APIMod.ToMod.
    // Example for a 1.5× DT map:        { "DT": { "speed_change": 1.5 } }
    // Example for Deflate (start 1.6):  { "DF": { "starting_size": 1.6 } }
    public Dictionary<string, Dictionary<string, object>> ModParameters
        = new Dictionary<string, Dictionary<string, object>>();

    [JsonProperty("BeatmapInfo")]
    public TournamentBeatmap? Beatmap;
}
```

Using `object` (not `double`) keeps the shape interchangeable with `APIMod.Settings`, so the helper that constructs a `Mod` instance with these settings (§5.2) doesn't need a numeric-only path. Newtonsoft round-trips nested `Dictionary<string, object>` natively in `bracket.json`.

**Round editor UI** (`RoundEditorScreen.BeatmapRow`): add a small "Mod settings" text field per beatmap row. Format: `acronym.setting=value` per line, e.g.
```
DT.speed_change=1.5
DF.starting_size=1.6
```
Values parsed permissively: try `double.TryParse` first, fall back to `bool.TryParse`, fall back to raw string. Free-form is faster to ship than per-mod typed UI; iterate if clunky in practice.

### 5.2 Extend `TournamentModIcon` to accept a configured `Mod`

`TournamentModIcon` today takes only a `string modAcronym`, looks up a custom tournament texture (`textures.Get($"Mods/{acronym}")`) and either renders that sprite or falls back to a vanilla `new ModIcon(mod, false) { Scale = 0.5f }`. Crucially, the embedded `ModIcon` is already constructed with `ShowExtendedInformation = true` (the third `ModIcon` constructor arg defaults to `true`), so the rate extender and cog corner badge are wired in — they just never trigger because `CreateModFromAcronym` produces a default-settings `Mod` instance, and `Mod.ExtendedIconInformation` / `Mod.HasNonDefaultSettings` evaluate to empty/false when settings are at default.

Two changes:

1. Add a `(Mod configuredMod)` constructor.
2. Gate the custom-texture lookup on `!mod.HasNonDefaultSettings` — a static branded sprite cannot surface a non-default speed change, so when settings are non-default we must fall through to the embedded `ModIcon`.

```csharp
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
        // A static sprite cannot surface a non-default speed change / setting,
        // so non-default mods fall through to the embedded ModIcon (which paints
        // the extender + cog).
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
        if (mod == null) return;

        AddInternal(new ModIcon(mod, false)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Scale = new Vector2(0.5f),
        });
    }
}
```

Behaviour matrix:

| Mod source | `HasNonDefaultSettings` | Custom-texture lookup | Result |
| --- | --- | --- | --- |
| `string acronym` (existing callers) | always false (default settings) | yes | Custom texture if present, else embedded `ModIcon` with empty extender / no cog (today's behaviour, unchanged). |
| `Mod` with default settings | false | yes | Custom texture if present, else embedded `ModIcon` with empty extender / no cog. |
| `Mod` with non-default settings (DT 1.6×, Deflate `starting_size=1.6`, etc.) | true | **skipped** | Embedded `ModIcon` paints the extender (rate inline as `1.60x`) and/or cog corner badge. |

The DT / non-default LM display requirements are then satisfied automatically:

- **DT/HT speed multipliers** — `ModRateAdjust.ExtendedIconInformation` returns `"1.60x"` (`FormattableString.Invariant($"{SpeedChange.Value:N2}x")` — ASCII lowercase `x`, two-decimal padding). With `ShowExtendedInformation = true` the embedded `ModIcon` paints the rate in its extender panel.
- **Other customized mods** (Deflate-with-non-default-starting-size, future LM mods) — `Mod.HasNonDefaultSettings` is true → `ModIcon.adjustmentMarker` is shown (cog-in-circle in the icon's top-right corner). No bespoke "Deflate ×1.6" chip; casters explain the specifics on stream.

#### Helper — `RoundBeatmapModFactory`

A new `osu.Game.Tournament/Components/RoundBeatmapModFactory.cs` parses `RoundBeatmap.Mods` (the bracket-acronym string) and applies any per-map settings via the `APIMod` round-trip. Render sites use this to obtain the configured `Mod` instances they hand to `TournamentModIcon`:

```csharp
public static class RoundBeatmapModFactory
{
    /// <summary>
    /// Parse the bracket acronym string ("HD" / "DT" / "HDDT" / "FM" / "") into
    /// configured Mod instances. Per-map <see cref="RoundBeatmap.ModParameters"/> entries
    /// are applied via APIMod round-trip, so the resulting mods have non-default
    /// settings where specified — and TournamentModIcon will route them through the
    /// embedded ModIcon (extender + cog) accordingly.
    /// </summary>
    public static IReadOnlyList<Mod> ConstructMods(RoundBeatmap rb, Ruleset ruleset)
    {
        // Centralise the acronym-string parse logic here (lifted from TournamentModIcon).
        var acronyms = ParseModString(rb.Mods);

        var result = new List<Mod>();

        foreach (var acronym in acronyms)
        {
            var mod = ruleset.CreateModFromAcronym(acronym);
            if (mod == null) continue;

            if (rb.ModParameters.TryGetValue(acronym, out var settings) && settings.Count > 0)
            {
                // Round-trip via APIMod to apply settings — same path the multiplayer client uses.
                var api = new APIMod { Acronym = acronym, Settings = new Dictionary<string, object>(settings) };
                mod = api.ToMod(ruleset);
            }

            result.Add(mod);
        }

        return result;
    }
}
```

#### Render-site updates

Both `TournamentBeatmapPanel` and `SongBar` already construct `TournamentModIcon` from `RoundBeatmap.Mods` (string acronym). Update them to construct via the factory and the new `(Mod)` constructor:

```csharp
// Was:  new TournamentModIcon(rb.Mods)
// Now:
var ruleset = rulesets.GetRuleset(ladderInfo.Ruleset.Value?.OnlineID ?? 0)?.CreateInstance();
if (ruleset == null) return;

foreach (var mod in RoundBeatmapModFactory.ConstructMods(rb, ruleset))
    flow.Add(new TournamentModIcon(mod));
```

Existing call sites that pass an acronym string directly (legacy, non-`RoundBeatmap`-bound usages) are unaffected — the string constructor is preserved.

`SongBar` already resolves `LadderInfo.CurrentMatch` and `IBindable<RulesetInfo>`, so the lookup is in scope. `TournamentBeatmapPanel` may need a small horizontal flow wrapper if its existing usage rendered a single `TournamentModIcon`; see the panel layout in §4.3.

**Mod settings change tracking:** the embedded `ModIcon` constructs a `ModSettingChangeTracker` internally (`ModIcon.cs:89`, `:218–225`) and updates the extender / cog on setting change. Each panel/songbar rebuild constructs fresh `TournamentModIcon` → fresh `ModIcon` → fresh tracker, so no extra wiring is needed; the existing dispose-on-rebuild handles the lifecycle.

### 5.3 Per-user mods in `MultiplayerMatchIPCInfo`

Today `MultiplayerMatchIPCInfo.updateModsFromRoom()` reads the *room's* `currentItem.RequiredMods` and exposes a single `LegacyMods Mods` bindable. For FreeMod (OG set) and LM bracket per-user surfacing, we need each player's chosen mods.

`MultiplayerRoomUser.Mods` is `IEnumerable<APIMod>`. Each `APIMod` has `Acronym` and `Settings : Dictionary<string, object>`.

**Extend `UserGameplayState`** with per-user mods:

```csharp
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
```

**Hook in `MultiplayerMatchIPCInfo`**: piggy-back on the already-subscribed `MultiplayerClient.RoomUpdated` handler. On every `RoomUpdated` tick, walk `multiplayerClient.Room.Users` and update each `userStates[userId].Mods` if the user's `MultiplayerRoomUser.Mods` reference (or contents) has changed. `RoomUpdated` fires whenever any room field changes, including a user's mod selection — so this is sufficient for v1, even though it does extra work on unrelated room changes (host change, settings edit, etc.). If profiling later shows this is hot, switch to a per-user event (e.g. a future `UserModsChanged` event surface) — that's a v2 optimization, not a blocker.

Concretely, factor out a helper:
```csharp
private void updateUserModsFromRoom()
{
    if (multiplayerClient.Room == null) return;

    foreach (var user in multiplayerClient.Room.Users)
    {
        if (!userStates.TryGetValue(user.UserID, out var existing))
            continue;

        // Cheap reference-equality skip if mods haven't changed.
        if (ReferenceEquals(existing.Mods, user.Mods))
            continue;

        userStates[user.UserID] = existing with { Mods = user.Mods.ToList() };
    }
}
```

Call from `onRoomUpdated()` alongside the existing `updateBeatmapFromRoom / updateModsFromRoom / updateChatChannelFromRoom`.

`onLoadRequested` resets each user's `UserGameplayState` to `Empty` — preserve that. Mods will be re-populated on the next `onRoomUpdated` (room state still carries each user's mods through load).

### 5.4 `IPCUserSnapshot.Mods` JSON field

Extend `IPCUserSnapshot` with a `Mods` field; extend `IPCSnapshot.SerializeToJson` to emit it. Schema:

```json
"users": [
  {
    "userId": 9876,
    "teamId": 1,
    "state": "playing",
    "role": "player",
    "score": 612345,
    "combo": 128,
    "accuracy": 0.9821,
    "hits": { "great": 456, "ok": 7, "meh": 1, "miss": 2 },
    "gameplayTimeMs": 47320,
    "mods": [
      { "acronym": "HD", "settings": {} },
      { "acronym": "DT", "settings": { "speed_change": 1.5 } }
    ]
  }
]
```

The shape mirrors lazer's `APIMod` JSON form, so consumers already familiar with lazer mod JSON can reuse code.

`IPCUserSnapshot`:
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

internal readonly record struct IPCUserModEntry(
    string Acronym,
    ImmutableDictionary<string, object> Settings);
```

`MultiplayerIPCWriter.BuildUserSnapshots` populates from `userStates[userId].Mods`. Settings keys/values come straight from `APIMod.Settings`. Equality on `IPCSnapshot` is by serialized-string compare (existing pattern), so the addition is automatically picked up by the dirty check.

### 5.5 Per-user mods in the gameplay overlay (`TournamentGameplayDisplay`)

In each `PlayerArea`, render a small horizontal flow of `TournamentModIcon` instances next to the player name — one per `APIMod` in `userStates[userId].Mods`. Subscribe via the existing `userStates` access path; rebuild the flow on changes. Active ruleset is resolved via `IBindable<RulesetInfo>` (already in scope on `TournamentGameplayDisplay`).

Each `APIMod` is converted to a `Mod` via `apiMod.ToMod(ruleset)` (lazer's existing path) and handed to `TournamentModIcon` via the new `(Mod)` constructor (§5.2). This produces:

- DT/HT icons with the rate inline (e.g. `1.50x`) via the embedded `ModIcon` extender — the custom-texture lookup is suppressed because `HasNonDefaultSettings` is true on a configured rate-adjust mod.
- Cog corner badge for any other customized mod (same suppression logic).
- Custom-texture branding (or plain `ModIcon` if no texture) for parameterless mods like NM, HD, HR — keeping visual consistency with how the same mods render in `SongBar` / `TournamentBeatmapPanel`.

When a player has no mods (NM-only player in FreeMod), the flow is empty — that's the signal "playing NM," consistent with how lazer's `ModDisplay` renders.

### 5.6 Phase 2 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneSongBar.TestTournamentModIconRendersDtRate` | Active beatmap with `ModParameters["DT"]["speed_change"] = 1.5` constructs `TournamentModIcon` via the new `(Mod)` constructor; the embedded `ModIcon`'s `extendedText.Text == "1.50x"` (delegates to lazer's `Mod.ExtendedIconInformation`, which formats `{value:N2}x`). |
| `TestSceneSongBar.TestTournamentModIconRendersCogForCustomized` | Beatmap with a non-rate customized mod (e.g. Deflate `starting_size=1.6`) renders a `TournamentModIcon` whose embedded `ModIcon` has `adjustmentMarker.Alpha == 1`. No bespoke chip. |
| `TestSceneTournamentModIcon.TestCustomTextureSuppressedForCustomisedMod` | When constructed with a `Mod` that has `HasNonDefaultSettings == true`, custom-texture lookup is skipped even if a `Mods/{acronym}` texture is registered, and the embedded `ModIcon` is rendered instead. |
| `TestSceneTournamentModIcon.TestCustomTexturePreservedForDefaultMod` | When constructed with a default-settings `Mod` (or via the legacy acronym-string constructor), custom-texture branding is preserved — the new constructor must not regress the existing path. |
| `TestSceneRoundEditorScreen.TestModParametersFreeForm` | Editing `DT.speed_change=1.5` populates `ModParameters["DT"]["speed_change"] = 1.5` (numeric); `MOD.flag=true` parses to bool; unknown values pass through as raw string. |
| `RoundBeatmapModFactoryTest.TestParseAndApply` | `ConstructMods` for `Mods="HDDT"` + `ModParameters={"DT":{"speed_change":1.5}}` yields a `[Hidden, DoubleTime]` list where the DoubleTime instance has `SpeedChange.Value == 1.5`. |
| `MultiplayerIPCWriterTest.TestPerUserMods` | Snapshot JSON contains `users[].mods` with per-user mod data; round-trips through equality dirty check (writes once when stable). |
| `TestSceneTournamentGameplayDisplay.TestPerUserModIcons` | Two users with different mod sets render distinct `TournamentModIcon` instances next to their player names. |
| `MultiplayerMatchIPCInfoTest.TestModUpdatePropagatesToUserStates` | When `MultiplayerRoomUser.Mods` changes, the corresponding `userStates[uid].Mods` updates on the next `RoomUpdated` tick. |

## 6. Phase 3 — 1v1 mode + match-complete + score-edit UI

### 6.1 `LadderInfo.Use1V1Mode` (port verbatim)

Add to `LadderInfo`:

```csharp
/// <summary>
/// When <c>true</c>, text elements referring to "Team"s are updated to "Player"s and
/// team players lists are hidden. Setup-screen toggle. Default off so legacy bracket.json
/// round-trips unchanged. Future extension candidates: collapse multi-row team scoreboards
/// to a single line, swap "Team Score" labels in MatchHeader, swap team flags for player avatars
/// in LadderScreen tiles.
/// </summary>
public Bindable<bool> Use1V1Mode = new Bindable<bool>(false);
```

Port the 5 call sites verbatim from the 2025 LGA tag:

| File | Behavior |
| --- | --- |
| `Screens/Ladder/Components/DrawableMatchTeam.cs` | Hide the players-under-team-name list when `Use1V1Mode` is true. |
| `Screens/TeamIntro/SeedingScreen.cs` (`LeftInfo`) | Adjust layout via the constructor flag. |
| `Screens/TeamIntro/TeamIntroScreen.cs` | Use `DrawableTeamTitleWithHeader` instead of `DrawableTeamWithPlayers`. |
| `Screens/TeamWin/TeamWinScreen.cs` | Same as TeamIntro. |
| `Screens/TournamentMatchScreen.cs` | `Use1V1Mode.BindValueChanged(_ => CurrentMatch.TriggerChange())` so dependents refresh on toggle. |

Setup screen (`Screens/Setup/SetupScreen.cs`): add a `LabelledSwitchButton` row labelled "1v1 mode", description "Text elements referring to 'Team's will be updated to 'Player's and team players lists will be hidden", bound to `LadderInfo.Use1V1Mode`. Mirrors the existing `LabelledSwitchButton` rows already in `reload()` (`UseMultiplayerSpectating` at line 111, `MuteUISounds` / others at lines 180, 186, 209) — `LabelledSwitchButton` is the established pattern for boolean toggles on this screen; `ActionableInfo` is reserved for buttons-with-side-info (resolution selector, tournament switcher).

For LGA 2026 brackets the operator turns this on at config time; default-off keeps non-LGA setups unaffected.

### 6.2 Match-complete auto-detect

In `GameplayScreen.updateState()`, the cumulative-scoring branch already increments `Team1Score` / `Team2Score` when a set finishes (existing code at lines 296–331). Add a `Completed` flip immediately after the increment:

```csharp
if (scores.Item1 > scores.Item2)
    CurrentMatch.Value.Team1Score.Value++;
else
    CurrentMatch.Value.Team2Score.Value++;

// Match completion auto-detect — LGA 2026 §3.6 first-to-3 rule.
int pointsToWin = CurrentMatch.Value.PointsToWin;
if (CurrentMatch.Value.Team1Score.Value >= pointsToWin
    || CurrentMatch.Value.Team2Score.Value >= pointsToWin)
{
    CurrentMatch.Value.Completed.Value = true;
}
```

`PointsToWin` is computed from `Round.BestOf` (existing, line 100 of `TournamentMatch.cs`). For LGA `BestOf = 5` → `PointsToWin = 3`.

**Nullable score values.** `Team1Score` and `Team2Score` are `Bindable<int?>` (`TournamentMatch.cs:39,46`) — the bindables are deliberately nullable so an unstarted match has no displayed score. The `>=` comparison above is safe without an explicit null check: C# nullable comparison evaluates `null >= 3` as `false`, which is exactly the right semantics here (a match that never reached `StartMatch()` cannot auto-complete). The existing `Team1Score.Value++` operates on `int?` and yields `null` for an unstarted score, but the cumulative branch only runs on `TourneyState.Ranking` after a real round of play — where `StartMatch()` has already zeroed both scores.

The existing `matchCompleteOverride` checkbox (line 157 of 2025 LGA `GameplayScreen`) is gone from the current branch; auto-detect plus the existing `match.Completed` bindable (which `TeamWinScreen` already keys off) is sufficient. If a ref needs to manually reset, they edit the score in the score-edit UI (§6.3) and the auto-detect un-flips.

**Edge case — auto-detect should not fire in warmup mode.** Wrap in `if (!warmup.Value)` (or rely on the existing branch which already early-returns from the cumulative block when `warmup.Value` is true at line 294). Cumulative branch already gated, so this is fine.

### 6.3 Operator score-edit UI

The score-edit block has **two sub-sections**, both added to `MapPoolScreen`'s control panel after the `"Reset"` button:

1. **Per-map score editor** — slot dropdown + red/blue score textboxes + Apply, mutates `CurrentMatch.Value.MapScores[slot]`.
2. **Per-team set-count editor** — two textboxes bound directly to `CurrentMatch.Value.Team1Score` / `Team2Score`.

Both are needed because set-win counters (`Team1Score` / `Team2Score`) are not automatically derived from `MapScores` — `GameplayScreen.updateState` only writes them on the `TourneyState.Ranking` transition. If a ref fixes a wrong per-map score after the fact, the set-panel display refreshes (via the existing `BindCollectionChanged` observers) but the team set count won't move; the ref nudges it directly via section 2.

```csharp
new ControlPanel.Spacer(),
new TournamentSpriteText { Text = "Edit map scores" },
mapScoreEditDropdown = new SettingsDropdown<string?>
{
    LabelText = "Slot",
    Items = …, // dynamically populated from current Round.Beatmaps[].SlotName — see re-bind below.
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
    Text = "Apply",
    Action = applyMapScoreEdit,
},

new ControlPanel.Spacer(),
new TournamentSpriteText { Text = "Edit set scores" },
team1SetScoreTextBox = new SettingsNumberBox
{
    LabelText = "Red set score",
    // Bind directly to match.Team1Score in (re)bind step.
},
team2SetScoreTextBox = new SettingsNumberBox
{
    LabelText = "Blue set score",
},
```

`applyMapScoreEdit`:
```csharp
private void applyMapScoreEdit()
{
    if (mapScoreEditDropdown.Current.Value is not string slot) return;
    if (CurrentMatch.Value == null) return;
    if (redScoreTextBox.Current.Value is not int red) return;
    if (blueScoreTextBox.Current.Value is not int blue) return;

    // MapScores value type is Tuple<long, long>; widening from int → long is implicit and lossless.
    // int.MaxValue (~2.1B) easily covers any realistic osu! score (lazer max ≈ 3.5M, stable max = 1M).
    CurrentMatch.Value.MapScores[slot] = new Tuple<long, long>(red, blue);
    // BindableDictionary fires CollectionChanged → TournamentSetPanel.SetMapResultDisplay.refreshScores
    // recomputes set winners for display. Match-level Team1Score/Team2Score are ref-edited via
    // section 2, not auto-derived (see top of §6.3).
}
```

`SettingsNumberBox` is declared `SettingsItem<int?>` (`SettingsNumberBox.cs:12`) — its `Current` is a `Bindable<int?>` whose value is `null` when the textbox is empty and `int` otherwise. No string parsing required at the call site.

**Widget style.** `SettingsDropdown<T>` and `SettingsNumberBox` come from `osu.Game.Overlays.Settings` and are sized for the full-width settings overlay (label-above-input, generous padding); `MapPoolScreen`'s existing `ControlPanel` children are compact (`TournamentSpriteText` / `TourneyButton` / `OsuCheckbox`). Keep `SettingsDropdown` / `SettingsNumberBox` anyway — the visual mismatch is acceptable for an operator-only panel where function trumps polish, and the alternative (wrapping `OsuDropdown<string>` + `OsuNumberBox` in custom labelled containers) is meaningful extra UI work for no functional gain. If the broadcast-graphic styling diverges enough to be visible to viewers, revisit in a follow-up.

**Re-binding when `CurrentMatch` changes.** Subscribe to `CurrentMatch.BindValueChanged(currentMatchChanged)` (the existing pattern on `TournamentMatchScreen`-derived screens) and inside the handler:

```csharp
private void currentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
{
    // …existing handling…

    // Rebuild dropdown items for the new match's round.
    var round = match.NewValue?.Round.Value;
    mapScoreEditDropdown.Items = round?.Beatmaps.Select(b => b.SlotName).ToArray() ?? Array.Empty<string>();

    // Re-bind set-score textboxes to the new match's bindables. SettingsNumberBox.Current is
    // Bindable<int?> (matches Team1Score/Team2Score exactly); assigning a Bindable to .Current is
    // the standard IHasCurrentValue pattern — internally it goes through BindableWithCurrent.
    if (match.NewValue != null)
    {
        team1SetScoreTextBox.Current = match.NewValue.Team1Score;
        team2SetScoreTextBox.Current = match.NewValue.Team2Score;
    }

    // Also re-bind on round-within-match changes.
    match.OldValue?.Round.ValueChanged -= roundChanged;
    if (match.NewValue != null)
        match.NewValue.Round.ValueChanged += roundChanged;
}

private void roundChanged(ValueChangedEvent<TournamentRound?> round)
{
    mapScoreEditDropdown.Items = round.NewValue?.Beatmaps.Select(b => b.SlotName).ToArray() ?? Array.Empty<string>();
}
```

The `ValueChanged` subscription on `Round` is required *in addition to* the `CurrentMatch` rebind — a ref can change a match's round in the editor without switching the current match, and the slot dropdown must follow. Unsubscribe from the old match's `Round.ValueChanged` to avoid leaking the handler when matches switch.

### 6.4 Phase 3 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneSetupScreen.TestUse1V1Toggle` | Toggle reflects in `LadderInfo.Use1V1Mode`. |
| `TestSceneTeamIntroScreen.TestUse1V1Display` | When `Use1V1Mode = true`, renders `DrawableTeamTitleWithHeader`; when false, `DrawableTeamWithPlayers`. |
| `TestSceneGameplayScreen.TestMatchAutoComplete` | Increment Team1 to 3 set wins → `match.Completed.Value` becomes true; under 3 → stays false. |
| `TestSceneMapPoolScreen.TestScoreEditApply` | Apply red=100, blue=50 to slot "NM1" → `match.MapScores["NM1"] == (100, 50)` and set panel updates. |
| `TestSceneMapPoolScreen.TestScoreEditTeamSetCounters` | Manually edit `Team1Score` / `Team2Score` via the small textboxes → bindable updates, header reflects. |

## 7. Phase 4 — MapPool 65/35 layout

### 7.1 Motivation

The current branch's MapPool screen stacks the map pool flow on top and the sets flow at bottom-centre, both spanning the full width. The 2025 LGA broadcast used a 65/35 split with a "Pool" heading on the left (containing the map flow) and a "Sets" heading on the right (containing the set-panels stack). **The split is required for the 2026 broadcast graphic and must ship before weekend 1**; refs report the stacked view clashes with the in-client songbar and the broadcast template assumes the column layout.

The "Phase 4" label is retained for continuity with the phase scoping table (§2) but the ship order is no longer last — see the rollout table (§8.3) for the updated deadline.

### 7.2 Layout changes (`MapPoolScreen.cs`)

Replace the two top-level child containers (`mapFlows`, `setsFlow`) with two sibling `GridContainer`s, mirroring the 2025 LGA tag:

```csharp
new GridContainer
{
    // Y/X/Width values verbatim from 2025.524.2-LGA+2025.424.0-week2 — the asymmetric
    // Y=90 (Pool) vs Y=170 (Sets) is intentional: 90 puts the Pool heading at the
    // existing 90–160 band so mapFlows resumes at ~Y=160 (matches the pre-split layout
    // and keeps updateDisplay's padding logic valid); 170 clears MatchHeader for Sets.
    Y = 90,
    X = 0f,
    Anchor = Anchor.TopLeft,
    RelativePositionAxes = Axes.X,
    Width = 0.65f,
    RelativeSizeAxes = Axes.X,
    AutoSizeAxes = Axes.Y,
    // TODO: verbatim port from 2025 LGA tag — Content has 2 rows (heading + flow) but
    // RowDimensions has only 1 entry. Verify behaviour at runtime: osu-framework may
    // pad missing entries with Distributed (which would conflict with AutoSizeAxes.Y),
    // or it may tolerate the mismatch. If broken, add a second `new Dimension(GridSizeMode.AutoSize)`.
    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
    Content = new[]
    {
        new Drawable[]
        {
            new TournamentSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Padding = new MarginPadding { Vertical = 4 },
                Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 18),
                Text = "Pool",
            },
        },
        new Drawable[]
        {
            mapFlows = new FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>
            {
                Anchor = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(10, 10),
                Direction = FillDirection.Vertical,
            },
        },
    },
},
new GridContainer
{
    // Y=170 verbatim from 2025 LGA tag (clears MatchHeader for the Sets column).
    Y = 170,
    X = 0.65f,
    Anchor = Anchor.TopLeft,
    RelativePositionAxes = Axes.X,
    Width = 0.35f,
    RelativeSizeAxes = Axes.X,
    AutoSizeAxes = Axes.Y,
    // TODO: same mismatch as Pool grid above — verify osu-framework behaviour at runtime.
    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
    Content = new[]
    {
        new Drawable[]
        {
            new TournamentSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Padding = new MarginPadding { Vertical = 4 },
                Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 18),
                Text = "Sets",
            },
        },
        new Drawable[]
        {
            setsFlow = new FillFlowContainer<TournamentSetPanel>
            {
                Anchor = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Spacing = new Vector2(10, 5),
                Direction = FillDirection.Full,
            },
        },
    },
},
```

The `setsFlow` loses the `Padding = new MarginPadding { Horizontal = 100 }` and `Anchor = BottomCentre / Y = -160` from the current stacked layout — those become unnecessary with the right-side column.

`updateDisplay()` keeps its layout-width logic but the threshold for `totalRows > 9 ? 0 : 100` (current padding switch) needs retuning because the available width drops to ~65% of the screen. Starting estimate: `Horizontal = totalRows > 7 ? 0 : 50` — threshold lowered because columns max out earlier in a narrower box, padding halved because absolute pixel margins should track the available width. Tune empirically from there using the test cases below.

The `TestFewMaps / TestJustEnoughMaps / TestManyMaps` test cases referenced in §7.3 originate from upstream PR ppy/osu#36200 and are ported to this branch as part of Phase 1's protect-icon work. Phase 4 reuses them as-is to verify the column-count threshold under the narrower 65% width — if Phase 1 didn't port them, Phase 4 must add them before retuning the padding switch.

### 7.3 Phase 4 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneMapPoolScreen.TestSplitLayoutPool` | Pool grid's `DrawWidth / mapPoolScreen.DrawWidth ≈ 0.65` (within `Precision.AlmostEquals` tolerance 0.01); a `TournamentSpriteText` with `Text == "Pool"` is present in its descendants. |
| `TestSceneMapPoolScreen.TestSplitLayoutSets` | Sets grid's `DrawWidth / mapPoolScreen.DrawWidth ≈ 0.35`; a `TournamentSpriteText` with `Text == "Sets"` is present in its descendants. |
| `TestSceneMapPoolScreen.TestFewMaps / TestJustEnoughMaps / TestManyMaps` (ported from PR #36200 in Phase 1) | Column count adapts to map count under the narrower 65% width — exercises the retuned `totalRows > 7 ? 0 : 50` padding switch. |

## 8. Cross-cutting concerns

### 8.1 `bracket.json` schema migration

| New field | Owner | Default | Round-trip behavior |
| --- | --- | --- | --- |
| `BeatmapChoice.Type = Protect (2)` | `BeatmapChoice` | n/a | Older files have only Pick=0/Ban=1; deserialize unchanged. New files containing `Protect` cannot be opened by older binaries — acceptable since this branch is LGA-only. |
| `TournamentMatch.Protects` | `TournamentMatch` | empty list | Newtonsoft tolerates missing field on old files; default to empty `ObservableCollection`. |
| `TournamentRound.ProtectCount` / `AllowPickingOpponentProtects` | `TournamentRound` | 0 / true | Bindable defaults apply when missing. |
| `RoundBeatmap.ModParameters` | `RoundBeatmap` | empty dict | Bindable defaults apply when missing. |
| `LadderInfo.Use1V1Mode` | `LadderInfo` | false | Bindable defaults apply when missing. |

No schema-version bump needed; all fields are additive with safe defaults.

### 8.2 Test execution

All tests live in `osu.Game.Tournament.Tests`. Existing CI already runs `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj`. New tests follow existing patterns:
- Behavioral tests (`TestPickBanOrder`, `TestProtectMechanism`, `TestMatchAutoComplete`, etc.) live in `TestSceneMapPoolScreen` / `TestSceneGameplayScreen` and use the same `TournamentScreenTestScene` base + `Ladder.CurrentMatch` setup.
- New visual scenes (`TestSceneTournamentBeatmapPanel.TestProtectIconRender`, `TestSceneSongBar.TestModParametersRender`) add visual-only steps using existing `TournamentTestScene` boilerplate.
- Unit-level tests for IPC schema (`MultiplayerIPCWriterTest.TestPerUserMods`) follow the existing `MultiplayerIPCWriterTest` pattern (golden JSON fixture diff).

Tests removed: the pre-LGA `TestPickBanOrder` / `TestBanOrderMultipleBans` / `TestMultipleTeamBans` are rewritten to LGA order rather than deleted, because they're useful coverage of the click → mode-advance loop; the new `TestProtectBanPickOrder` is the LGA-specific one.

### 8.3 Rollout / phasing for LGA 2026 deadlines

| Phase | Target ship date | Hard requirement for LGA |
| --- | --- | --- |
| Phase 1 (pick/ban + Protect) | by 2026-05-15 (day before bracket stage) | **Yes.** Refs cannot run an LGA bracket draft without Protect. |
| Phase 2 (mod params + per-user mods) | by 2026-05-15 | **Yes** for DT bracket display; **soft yes** for FreeMod OG (only relevant if bracket reaches set 5 → tiebreaker). |
| Phase 3 (1v1 mode + match complete + score edit) | by 2026-05-22 (between weekends) | **Yes** for 1v1 mode (cosmetic but visible on every screen); **yes** for match-complete (operator quality of life); **yes** for score-edit (ref recovery tooling). |
| Phase 4 (MapPool 65/35 layout) | by 2026-05-15 (day before bracket stage) | **Yes** — broadcast graphic depends on the column layout; must ship before weekend 1. |

If schedule slips, the order of fall-through is Phase 3 sub-items individually → Phase 2 sub-items individually. Phase 1 and Phase 4 are both non-negotiable for the bracket draft (Phase 1 for ref draft mechanics, Phase 4 for broadcast).

### 8.4 Implementation plan

Each phase becomes its own implementation plan, written by the `superpowers:writing-plans` skill, executed independently. Phase 1 should be the first plan; **Phase 4 should be the second plan** (now that it is a weekend-1 hard requirement — see §8.3). Phases 2 and 3 may be planned in parallel after Phase 1 + Phase 4 land (no shared file collisions between any of the four). Phase 4 touches only `MapPoolScreen.cs` layout code and is functionally orthogonal to the protect mechanic, so it can ship as its own PR without coordinating with Phases 2-3.

### 8.5 Out-of-scope items revisited (decision log)

| Item | Reason |
| --- | --- |
| Tactical-timeout UI | Operator-side tooling; refs can use a stopwatch for v1. Revisit if 2026 broadcast wants on-screen timer. |
| Set-winner manual override on `TournamentSetPanel` | Cumulative scoring auto-determines; LGA 2025's right-click override was deliberately dropped per memory `project_lga2025_to_lga2026.md`. |
| Ladder-editor reset-teams confirmation dialog | UX nicety, not LGA-specific. |
| `TeamScoreCumulative` (LGA 2025 component) | `MatchHeader.MatchCumulativeScoreDiffCounter` already covers the same signal on this branch. |
| Backwards compatibility for `Use1V1Mode = false` on this LGA-only branch | Not actively maintained, but defaults preserve round-trip safety. |

## 9. Open questions

None at design lock. All decisions resolved in brainstorming on 2026-05-10.

If new questions arise during implementation (notably around `MultiplayerClient.UserModsChanged` event surface availability, or `APIMod.Settings` JSON-shape edge cases), they're surfaced in the per-phase implementation plan rather than re-opened here.
