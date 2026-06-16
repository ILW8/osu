# Capped Cumulative Scoring with Custom Multipliers — Design

- **Date:** 2026-06-16
- **Branch:** `feature/tournament-capped-scoring` (based on `ILW8/feature/tournament-protects--mwc7k-release`)
- **Status:** Approved (design); implementation pending

## Context

This branch is the MWC7K release line: upstream osu! tournament + a **protects** feature
(`ChoiceType.Protect`, mappool protect UI) + a chroma-wipe gameplay transition + a
mappool→gameplay transition fix. Its scoring is plain **best-of-N per-map wins**: on each
map result (`GameplayScreen.updateState()`, `State == Ranking`) it does
`Team1Score++` / `Team2Score++`, and match completion is auto-computed in
`DrawableTournamentMatch` (done when `total ≥ BestOf` or a team passes `BestOf/2`).

We are running a new tournament modelled on **SSR**, whose scoring is *cumulative score
gap, capped per map*. SSR (tag `2025.724.0-SSR+2025.424.0`) implemented this on its
stable-IPC path as `Team1Score += min(50_000, |Score1 − Score2|)`, with an
unrecoverable-lead auto-complete, an SSR-specific per-team cumulative text counter
(`TeamScoreCumulative`), and a centered cumulative delta display
(`MatchCumulativeScoreDiffCounter` + winning triangles in `MatchHeader`).

This design adds an analogous, generalised scoring mode to the current branch, with
**three hard-coded tiers** (regular / protected / Final Scoring) and configurable per-round
map counts.

## Goals

- Add a **capped cumulative scoring** mode: each played map adds a capped, multiplied
  score gap to the leading team's running total.
- Support three map tiers with hard-coded multipliers and caps.
- Auto-complete matches by **unrecoverable lead**, with a conditional **Final Scoring** map.
- Replace the win-count stars with an SSR-style cumulative **text** display and a centered
  cumulative **delta** display.
- Keep everything behind a toggle so the client can still run normal best-of matches.

## Non-goals (out of scope)

- **Mappool guardrails**: limiting regular picks to `PicksCount − 1`, and surfacing /
  auto-selecting the Final Scoring map. Completion + display make the overlay correct under
  normal referee operation; pick-limit enforcement and TB auto-selection are a separate
  mappool-UX follow-up.
- Editor-configurable multipliers/caps. The tier values are hard-coded constants
  (they are fixed match rules and not expected to change).
- Any change to non-cumulative (best-of) behavior.

## Scoring model

When `CumulativeScore` is on, on each non-warmup map result the contribution is added to the
**leader of that map** (the team with the higher raw score on the just-played map):

```
contribution = round( tier.Multiplier × min( |score1 − score2|, tier.BaseCap ) )
leaderTotal += contribution
```

This is equivalent to "effective max lead = baseCap × multiplier", since
`min(baseCap, gap) × mult = min(baseCap × mult, gap × mult)`.

### Tiers (hard-coded)

| Tier          | Multiplier | Base cap (raw gap) | Effective max lead / map |
|---------------|-----------:|-------------------:|-------------------------:|
| Regular       |       1.0  |            10,000  |                  10,000  |
| Protected     |       0.85 |            10,000  |                   8,500  |
| Final Scoring (TB) | 1.1   |            25,000  |                  27,500  |

### Tier resolution (precedence)

For the played map (`ipc.Beatmap`, looked up as a `RoundBeatmap` in `CurrentMatch.Round.Beatmaps`):

1. **Final Scoring** if `RoundBeatmap.Mods == "TB"` (case-insensitive).
2. else **Protected** if `CurrentMatch.PicksBans` contains a `ChoiceType.Protect` entry whose
   `BeatmapID` matches the played map (protect by *either* team counts).
3. else **Regular**.

### Edge cases

- Tie on a map (`score1 == score2`): gap `0` → contribution `0` → no change.
- Contribution rounded to nearest integer (`Team1Score`/`Team2Score` are `int?`); capped
  totals stay small (≤ 27,500/map), so `int` is safe.
- Warmup maps are excluded (existing `warmup` guard in `updateState`).

## Completion model

Each round defines a maximum number of maps `N` (**including** the Final Scoring map):
e.g. 7 (Play-offs / Ro64 / Ro32), 9 (Ro16 / QF), 11 (SF / F / GF). These are per-round data,
not hard-coded. So there are up to `N − 1` regular picks plus the Final Scoring map as a
conditional last slot.

After each played map:

```
mapsPlayed = CurrentMatch.PicksBans.Count(Type == Pick)
x          = PicksCount − mapsPlayed          // maps remaining
lead       = |Team1Total − Team2Total|

decided = (x <= 0) || (lead >= 25_000 * x)
```

- If `decided`, set `CurrentMatch.Completed = true`; the higher total wins. The existing
  auto-advance (→ `TeamWinScreen`) handles screen transition.
- If not decided and `x == 1` (after `N − 1` regular maps with `lead < 25,000`), the match
  stays in the mappool; the referee picks the **Final Scoring** (TB) map. After it is played,
  `x == 0` → decided.

The `25,000 × x` threshold uses the raw `25,000` per remaining map exactly as specified in
the match rules (not the 27,500 effective TB value).

In cumulative mode, `DrawableTournamentMatch`'s best-of auto-completion (which treats
`Team1Score`/`Team2Score` as win counts) is **bypassed**; `Completed` is set by the gameplay
flow instead.

## Data-model changes

- **`LadderInfo.CumulativeScore`** — new `BindableBool`, default **true** for this build,
  surfaced as a toggle in `SetupScreen`.
- **`TournamentRound.BestOf` → `PicksCount`** — rename, with `[JsonProperty("BestOf")]` so
  existing `bracket.json` files still deserialize. Same numeric field; in cumulative mode it
  means "total maps incl. Final Scoring", in best-of mode it keeps its current meaning.
  Ripples (mechanical): `RoundEditorScreen` label, `TournamentMatch.PointsToWin`,
  `DrawableTournamentMatch`.
- Team totals continue to use `TournamentMatch.Team1Score` / `Team2Score` (`int?`).

## Components

### `CumulativeScoreCalculator` (new, pure / unit-testable)

`osu.Game.Tournament/Scoring/CumulativeScoreCalculator.cs`. No framework/UI dependencies.

- A `ScoreTier` value (`double Multiplier`, `int BaseCap`) and the three hard-coded constants.
- `ResolveTier(RoundBeatmap playedMap, TournamentMatch match)` → `ScoreTier` (precedence above).
- `Contribution(long score1, long score2, ScoreTier tier)` → `(bool winnerIsTeam1, int points)`.
- `IsDecided(int team1Total, int team2Total, int mapsPlayed, int picksCount)` → `bool`.

### `GameplayScreen.updateState()` integration

When `State == Ranking` and not warmup and `CumulativeScore` is on:
1. Resolve the played map's tier.
2. Compute the contribution and add it to the leader's running total.
3. Evaluate `IsDecided` and set `CurrentMatch.Completed` accordingly.

The existing auto-advance to `TeamWinScreen` / `MapPoolScreen` is reused unchanged.

## Display

Active only when `CumulativeScore` is on:

- **Per-team cumulative total** — port SSR's `TeamScoreCumulative` text counter into
  `TeamDisplay` / `TeamScoreDisplay`; hide the win-count stars.
- **Centered delta** — port SSR's `MatchCumulativeScoreDiffCounter` + left/right winning
  triangles into `MatchHeader`; shows `|Team1Total − Team2Total|`, triangle points at the
  leader.
- Both bind directly to `Team1Score` / `Team2Score` (the **simplified** binding used after
  SSR commit `c4b042533a`), not SSR's MatchSet / lazer-IPC path — matching this branch's
  single `MatchIPCInfo`.

## Testing

- **Unit** (`CumulativeScoreCalculator`):
  - Tier resolution: regular / protected / Final-Scoring, precedence (TB over protect),
    protect by either team.
  - `Contribution`: cap boundaries (regular gap ≥ 10k → 10,000; protect → 8,500; TB gap ≥ 25k
    → 27,500), sub-cap scaling by multiplier, rounding, tie → 0, correct leader.
  - `IsDecided`: `lead ≥ 25,000 × x` boundary across several `mapsPlayed`/`PicksCount`, `x ≤ 0`,
    near-tie.
- **Visual / integration**: a `TestSceneGameplayScreen` path with cumulative on — totals
  accumulate, delta display updates, completion auto-advances. Update existing tournament test
  scenes for the `PicksCount` rename.

## Key files

- `osu.Game.Tournament/Scoring/CumulativeScoreCalculator.cs` *(new)*
- `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs`
- `osu.Game.Tournament/Models/LadderInfo.cs`
- `osu.Game.Tournament/Models/TournamentRound.cs`
- `osu.Game.Tournament/Screens/Gameplay/Components/TeamScoreCumulative.cs` *(port from SSR)*
- `osu.Game.Tournament/Screens/Gameplay/Components/MatchHeader.cs`
- `osu.Game.Tournament/Screens/Gameplay/Components/TeamDisplay.cs` / `TeamScore*.cs`
- `osu.Game.Tournament/Screens/Ladder/Components/DrawableTournamentMatch.cs` (bypass best-of)
- `osu.Game.Tournament/Screens/Setup/SetupScreen.cs` (toggle)
- `osu.Game.Tournament/Screens/Editors/RoundEditorScreen.cs` (`PicksCount` label)
