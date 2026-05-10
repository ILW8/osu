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

This phase relies on lazer's existing `ModIcon` infrastructure (`osu.Game/Rulesets/UI/ModIcon.cs`) for all customized-mod rendering — both the DT/HT speed-multiplier display and the "this mod has customized settings" indicator. No bespoke chip rendering, no `FormatModParameter` helper. The spec ships per-map mod *storage* and the round-editor input plus the existing-icon rendering, plus per-user mod plumbing through IPC.

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

### 5.2 Render via lazer's `ModIcon` — DT rate inline, cog for everything else

Both rendering surfaces (`SongBar` and `TournamentBeatmapPanel`) use lazer's existing `ModIcon` for any map whose `RoundBeatmap` has `ModParameters` populated. The icon already handles both LGA display requirements out of the box:

- **DT/HT speed multipliers** — `ModRateAdjust.ExtendedIconInformation` returns `"1.5×"` (or whatever the configured rate is). With `ModIcon.ShowExtendedInformation = true`, the icon paints the rate in the attached extender panel to the right of the hex.
- **Other customized mods** (Deflate-with-non-default-starting-size, future LM mods, etc.) — `Mod.HasNonDefaultSettings` is true → `ModIcon.adjustmentMarker` is shown (cog-in-circle in the icon's top-right corner). No bespoke "Deflate ×1.6" chip; casters explain the specifics on stream.

**Helper** in a new `osu.Game.Tournament/Components/RoundBeatmapModFactory.cs`:

```csharp
public static class RoundBeatmapModFactory
{
    /// <summary>
    /// Construct fully-configured Mod instances for a RoundBeatmap. Returns the parsed mods from
    /// the bracket string (e.g. "HD"+"DT" → Hidden + DoubleTime) with any per-map
    /// settings from <see cref="RoundBeatmap.ModParameters"/> applied via APIMod round-trip.
    /// </summary>
    public static IReadOnlyList<Mod> ConstructMods(RoundBeatmap rb, Ruleset ruleset)
    {
        // 1. Parse the acronym string ("HDDT" / "HD,DT" / "DT" / "" / "FM") to bracket acronyms.
        //    Keep existing TournamentModIcon parsing logic; centralise it here.
        var acronyms = ParseModString(rb.Mods);

        var result = new List<Mod>();

        foreach (var acronym in acronyms)
        {
            var mod = ruleset.CreateModFromAcronym(acronym);
            if (mod == null) continue;

            if (rb.ModParameters.TryGetValue(acronym, out var settings) && settings.Count > 0)
            {
                // Round-trip via APIMod to apply settings — same path as the multiplayer client uses.
                var api = new APIMod { Acronym = acronym, Settings = new Dictionary<string, object>(settings) };
                mod = api.ToMod(ruleset);
            }

            result.Add(mod);
        }

        return result;
    }
}
```

**`TournamentBeatmapPanel`** — current branch already constructs a `TournamentModIcon` from `RoundBeatmap.Mods` (string acronym). Replace with: when `ModParameters` is populated, construct each `Mod` via `RoundBeatmapModFactory.ConstructMods(...)` and render a horizontal flow of `ModIcon` instances with `ShowExtendedInformation = true`. When `ModParameters` is empty, keep using `TournamentModIcon` (lighter-weight, already styled for the panel).

**`SongBar`** — same swap. The bottom-mods row currently renders a chain of `TournamentModIcon`. Switch to `ModIcon` for the active beatmap when its `RoundBeatmap` has parameters; otherwise keep `TournamentModIcon`. Resolve `LadderInfo.CurrentMatch` and `IBindable<RulesetInfo>` (already resolved on `SongBar`) to look up the active `RoundBeatmap` and construct the mods.

The split (panel: lazer `ModIcon` only when parameters present, otherwise `TournamentModIcon`) is intentional — it's the smallest change to the existing rendering for the parameterless case (NM/HD/HR mappool entries) while picking up lazer's full mod display for the parameterised case (DT/LM in 2026). If down the road we want a single uniform icon style, replace `TournamentModIcon` wholesale; not in scope here.

**Mod settings change tracking:** `ModIcon` constructs a `ModSettingChangeTracker` internally (line 89, line 218–225 of `ModIcon.cs`) and updates the extended-info / cog state on setting change. Since we construct fresh `Mod` instances on each panel rebuild and each rebuild creates new `ModIcon` instances, no extra wiring is needed; existing dispose-on-rebuild handles the lifecycle.

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

In each `PlayerArea`, render a small horizontal flow of `ModIcon` instances next to the player name — one per `APIMod` in `userStates[userId].Mods`. Subscribe via the existing `userStates` access path; rebuild the flow on changes. Active ruleset is resolved via `IBindable<RulesetInfo>` (already in scope on `TournamentGameplayDisplay`).

Each `APIMod` is converted to a `Mod` via `apiMod.ToMod(ruleset)` (lazer's existing path) before being passed to `ModIcon` with `ShowExtendedInformation = true`. This produces:

- DT/HT chips with the rate inline (e.g. `1.5×`).
- Cog corner badge for any other customized mod.
- Plain icon for parameterless mods (NM, HD, HR, FreeMod combos).

When a player has no mods (NM-only player in FreeMod), the flow is empty — that's the signal "playing NM," consistent with how lazer's `ModDisplay` renders.

### 5.6 Phase 2 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneSongBar.TestModIconRendersDtRate` | Active beatmap with `ModParameters["DT"]["speed_change"] = 1.5` renders a `ModIcon` whose `extendedText.Text == "1.5×"` (delegates to lazer's `Mod.ExtendedIconInformation`). |
| `TestSceneSongBar.TestModIconRendersCogForCustomized` | Beatmap with a non-rate customized mod (e.g. Deflate `starting_size=1.6`) renders a `ModIcon` with `adjustmentMarker.Alpha == 1`. No bespoke chip. |
| `TestSceneTournamentBeatmapPanel.TestModIconForParameterised` | Mappool panel uses lazer `ModIcon` when `ModParameters` is non-empty; falls back to `TournamentModIcon` when empty. |
| `TestSceneRoundEditorScreen.TestModParametersFreeForm` | Editing `DT.speed_change=1.5` populates `ModParameters["DT"]["speed_change"] = 1.5` (numeric); `MOD.flag=true` parses to bool; unknown values pass through as raw string. |
| `RoundBeatmapModFactoryTest.TestParseAndApply` | `ConstructMods` for `Mods="HDDT"` + `ModParameters={"DT":{"speed_change":1.5}}` yields a `[Hidden, DoubleTime]` list where the DoubleTime instance has `SpeedChange.Value == 1.5`. |
| `MultiplayerIPCWriterTest.TestPerUserMods` | Snapshot JSON contains `users[].mods` with per-user mod data; round-trips through equality dirty check (writes once when stable). |
| `TestSceneTournamentGameplayDisplay.TestPerUserModIcons` | Two users with different mod sets render distinct `ModIcon` instances next to their player names. |
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

Setup screen (`Screens/Setup/SetupScreen.cs`): add a `LabelledSwitchButton` row (or whatever the current setup-screen pattern is — likely an `ActionableInfo` block) labelled "1v1 mode", description "Text elements referring to 'Team's will be updated to 'Player's and team players lists will be hidden", bound to `LadderInfo.Use1V1Mode`.

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

The existing `matchCompleteOverride` checkbox (line 157 of 2025 LGA `GameplayScreen`) is gone from the current branch; auto-detect plus the existing `match.Completed` bindable (which `TeamWinScreen` already keys off) is sufficient. If a ref needs to manually reset, they edit the score in the score-edit UI (§6.3) and the auto-detect un-flips.

**Edge case — auto-detect should not fire in warmup mode.** Wrap in `if (!warmup.Value)` (or rely on the existing branch which already early-returns from the cumulative block when `warmup.Value` is true at line 294). Cumulative branch already gated, so this is fine.

### 6.3 Operator score-edit UI

Add to `MapPoolScreen`'s control panel, immediately after the `"Reset"` button:

```csharp
new ControlPanel.Spacer(),
new TournamentSpriteText { Text = "Edit map scores" },
mapScoreEditDropdown = new SettingsDropdown<string?>
{
    LabelText = "Slot",
    Items = …, // dynamically populated from current Round.Beatmaps[].SlotName
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
```

`applyMapScoreEdit`:
```csharp
private void applyMapScoreEdit()
{
    if (mapScoreEditDropdown.Current.Value is not string slot) return;
    if (CurrentMatch.Value == null) return;
    if (!long.TryParse(redScoreTextBox.Current.Value, out long red)) return;
    if (!long.TryParse(blueScoreTextBox.Current.Value, out long blue)) return;

    CurrentMatch.Value.MapScores[slot] = new Tuple<long, long>(red, blue);
    // No further action — TournamentSetPanel observes MapScores via BindCollectionChanged.
    // GameplayScreen.updateState only writes on TourneyState.Ranking, so this manual edit
    // does not bump the set-win counters.
}
```

When `MapScores[slot]` is mutated, the existing `BindCollectionChanged` observers (in `TournamentSetPanel.SetMapResultDisplay.refreshScores`) recompute set winners. Set-win counters (`Team1Score`/`Team2Score`) are *not* automatically derived from `MapScores`; if a ref edits a score that flips a set winner, they'd also need to nudge the set-win counter. Document this nuance: the score-edit UI fixes per-map cumulative display; if the set-point totals are wrong, refs edit those separately via the same UI (treat `Team1Score`/`Team2Score` as ref-editable bindables — wire them to a separate small textbox pair).

Alternative recommendation: **also expose** a "Team 1 set score" / "Team 2 set score" pair of textboxes (small, near the existing edit block) that bind to `match.Team1Score` / `match.Team2Score`. Keeps the surface area cohesive.

`mapScoreEditDropdown.Items` is repopulated when `CurrentMatch.Round` changes; subscribe to `CurrentMatch.Value.Round.ValueChanged` and rebuild from `Round.Value.Beatmaps.Select(b => b.SlotName)`.

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

The current branch's MapPool screen stacks the map pool flow on top and the sets flow at bottom-centre, both spanning the full width. The 2025 LGA broadcast used a 65/35 split with a "Pool" heading on the left (containing the map flow) and a "Sets" heading on the right (containing the set-panels stack). The split is preferred for the broadcast graphic; refs report the stacked view occasionally clashes with the in-client songbar.

### 7.2 Layout changes (`MapPoolScreen.cs`)

Replace the two top-level child containers (`mapFlows`, `setsFlow`) with two sibling `GridContainer`s, mirroring the 2025 LGA tag:

```csharp
new GridContainer
{
    Y = 90,
    X = 0f,
    Anchor = Anchor.TopLeft,
    RelativePositionAxes = Axes.X,
    Width = 0.65f,
    RelativeSizeAxes = Axes.X,
    AutoSizeAxes = Axes.Y,
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
    Y = 170,
    X = 0.65f,
    Anchor = Anchor.TopLeft,
    RelativePositionAxes = Axes.X,
    Width = 0.35f,
    RelativeSizeAxes = Axes.X,
    AutoSizeAxes = Axes.Y,
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

`updateDisplay()` keeps its layout-width logic but the threshold for `totalRows > 9 ? 0 : 100` (current padding switch) shrinks because the available width is now ~65% of the screen. Re-tune empirically; not specified here, and there are existing "TestFewMaps / TestJustEnoughMaps / TestManyMaps" cases on PR #36200 that exercise the column-count switching behavior.

### 7.3 Phase 4 testing

| Test | What it verifies |
| --- | --- |
| `TestSceneMapPoolScreen.TestSplitLayoutPool` | Pool flow occupies left 65% width; "Pool" heading visible. |
| `TestSceneMapPoolScreen.TestSplitLayoutSets` | Sets flow occupies right 35% width; "Sets" heading visible. |
| `TestSceneMapPoolScreen.TestFewMaps / TestManyMaps` (existing on PR #36200) | Column count adapts to map count under the narrower 65% width. |

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
| Phase 4 (MapPool 65/35 layout) | by 2026-05-22 | **No** — broadcast preference, can ship after weekend 1 if needed. |

If schedule slips, the order of fall-through is Phase 4 → Phase 3 (subitems individually) → Phase 2 sub-items individually. Phase 1 is non-negotiable for the bracket draft.

### 8.4 Implementation plan

Each phase becomes its own implementation plan, written by the `superpowers:writing-plans` skill, executed independently. Phase 1 should be the first plan. Phases 2 and 3 may be planned in parallel after Phase 1 lands (no shared file collisions). Phase 4 is purely visual and can be planned last.

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
