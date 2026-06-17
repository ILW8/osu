// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Scoring
{
    /// <summary>
    /// Pure scoring logic for capped cumulative scoring mode. No framework/UI dependencies.
    /// Each played map adds <c>round(multiplier * min(gap, baseCap))</c> to the map leader's running total.
    /// </summary>
    public static class CumulativeScoreCalculator
    {
        /// <summary>
        /// A scoring tier: the raw per-map gap is capped at <see cref="BaseCap"/>, then scaled by <see cref="Multiplier"/>.
        /// </summary>
        public readonly struct ScoreTier : IEquatable<ScoreTier>
        {
            public readonly double Multiplier;
            public readonly int BaseCap;

            public ScoreTier(double multiplier, int baseCap)
            {
                Multiplier = multiplier;
                BaseCap = baseCap;
            }

            public bool Equals(ScoreTier other) => Multiplier.Equals(other.Multiplier) && BaseCap == other.BaseCap;
            public override bool Equals(object? obj) => obj is ScoreTier other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Multiplier, BaseCap);
        }

        public static readonly ScoreTier Regular = new ScoreTier(1.0, 10_000);
        public static readonly ScoreTier Protected = new ScoreTier(0.85, 10_000);
        public static readonly ScoreTier FinalScoring = new ScoreTier(1.1, 25_000);

        /// <summary>
        /// Raw (unmultiplied) maximum lead a single remaining map can swing. Used for the
        /// unrecoverable-lead completion check, per the match rules.
        /// </summary>
        public const int MAX_LEAD_PER_MAP = 25_000;

        /// <summary>
        /// Resolves the scoring tier for a played map. Precedence:
        /// Final Scoring (<c>Mods == "TB"</c>, case-insensitive) &gt; Protected (a <see cref="ChoiceType.Protect"/>
        /// entry by either team matches the map) &gt; Regular. A null map resolves to Regular.
        /// </summary>
        public static ScoreTier ResolveTier(RoundBeatmap? playedMap, TournamentMatch match)
        {
            if (playedMap == null)
                return Regular;

            if (string.Equals(playedMap.Mods, "TB", StringComparison.OrdinalIgnoreCase))
                return FinalScoring;

            if (match.PicksBans.Any(pb => pb.Type == ChoiceType.Protect && pb.BeatmapID == playedMap.ID))
                return Protected;

            return Regular;
        }

        /// <summary>
        /// Computes a played map's contribution to its leader's running total.
        /// </summary>
        /// <returns>
        /// <c>winnerIsTeam1</c>: whether team 1 had the higher raw score on the map (ties report team 1, but
        /// contribute 0 points). <c>points</c>: the rounded, capped, multiplied points to add to that leader.
        /// </returns>
        public static (bool winnerIsTeam1, int points) Contribution(long score1, long score2, ScoreTier tier)
        {
            long gap = Math.Abs(score1 - score2);
            long capped = Math.Min(gap, tier.BaseCap);
            int points = (int)Math.Round(tier.Multiplier * capped, MidpointRounding.AwayFromZero);

            return (score1 >= score2, points);
        }

        /// <summary>
        /// Whether the match is decided: no maps remain, or the lead is unrecoverable across the maps still
        /// to be played (<c>lead &gt;= MAX_LEAD_PER_MAP * mapsRemaining</c>).
        /// </summary>
        public static bool IsDecided(int team1Total, int team2Total, int mapsPlayed, int picksCount)
        {
            int mapsRemaining = picksCount - mapsPlayed;
            int lead = Math.Abs(team1Total - team2Total);

            return mapsRemaining <= 0 || lead >= MAX_LEAD_PER_MAP * mapsRemaining;
        }
    }
}
