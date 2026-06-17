// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Scoring;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class CumulativeScoreCalculatorTest
    {
        private static TournamentMatch matchWithProtect(int beatmapId, TeamColour team = TeamColour.Red)
        {
            var match = new TournamentMatch();
            match.PicksBans.Add(new BeatmapChoice { BeatmapID = beatmapId, Team = team, Type = ChoiceType.Protect });
            return match;
        }

        // ---- ResolveTier ----

        [Test]
        public void TestResolveRegularTier()
        {
            var map = new RoundBeatmap { ID = 1, Mods = string.Empty };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, new TournamentMatch()), Is.EqualTo(CumulativeScoreCalculator.Regular));
        }

        [Test]
        public void TestResolveProtectedTier()
        {
            var map = new RoundBeatmap { ID = 7, Mods = "HD" };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, matchWithProtect(7)), Is.EqualTo(CumulativeScoreCalculator.Protected));
        }

        [Test]
        public void TestProtectByEitherTeamCounts()
        {
            var map = new RoundBeatmap { ID = 7, Mods = "HD" };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, matchWithProtect(7, TeamColour.Blue)), Is.EqualTo(CumulativeScoreCalculator.Protected));
        }

        [Test]
        public void TestResolveFinalScoringTier()
        {
            var map = new RoundBeatmap { ID = 9, Mods = "TB" };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, new TournamentMatch()), Is.EqualTo(CumulativeScoreCalculator.FinalScoring));
        }

        [Test]
        public void TestFinalScoringIsCaseInsensitive()
        {
            var map = new RoundBeatmap { ID = 9, Mods = "tb" };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, new TournamentMatch()), Is.EqualTo(CumulativeScoreCalculator.FinalScoring));
        }

        [Test]
        public void TestFinalScoringTakesPrecedenceOverProtect()
        {
            var map = new RoundBeatmap { ID = 9, Mods = "TB" };
            Assert.That(CumulativeScoreCalculator.ResolveTier(map, matchWithProtect(9)), Is.EqualTo(CumulativeScoreCalculator.FinalScoring));
        }

        [Test]
        public void TestNullMapResolvesRegular()
        {
            Assert.That(CumulativeScoreCalculator.ResolveTier(null, matchWithProtect(7)), Is.EqualTo(CumulativeScoreCalculator.Regular));
        }

        // ---- Contribution ----

        [Test]
        public void TestRegularCap()
        {
            var (winnerIsTeam1, points) = CumulativeScoreCalculator.Contribution(50_000, 0, CumulativeScoreCalculator.Regular);
            Assert.That(winnerIsTeam1, Is.True);
            Assert.That(points, Is.EqualTo(10_000));
        }

        [Test]
        public void TestProtectedCap()
        {
            var (_, points) = CumulativeScoreCalculator.Contribution(50_000, 0, CumulativeScoreCalculator.Protected);
            Assert.That(points, Is.EqualTo(8_500));
        }

        [Test]
        public void TestFinalScoringCap()
        {
            var (_, points) = CumulativeScoreCalculator.Contribution(100_000, 0, CumulativeScoreCalculator.FinalScoring);
            Assert.That(points, Is.EqualTo(27_500));
        }

        [Test]
        public void TestSubCapScaling()
        {
            // team 2 leads by 5000; protected multiplier 0.85 -> 4250
            var (winnerIsTeam1, points) = CumulativeScoreCalculator.Contribution(0, 5_000, CumulativeScoreCalculator.Protected);
            Assert.That(winnerIsTeam1, Is.False);
            Assert.That(points, Is.EqualTo(4_250));
        }

        [Test]
        public void TestRoundingHalfAwayFromZero()
        {
            // protected: 0.85 * 10 = 8.5 -> 9
            var (_, points) = CumulativeScoreCalculator.Contribution(10, 0, CumulativeScoreCalculator.Protected);
            Assert.That(points, Is.EqualTo(9));
        }

        [Test]
        public void TestTieContributesZero()
        {
            var (_, points) = CumulativeScoreCalculator.Contribution(12_345, 12_345, CumulativeScoreCalculator.Regular);
            Assert.That(points, Is.EqualTo(0));
        }

        [Test]
        public void TestLeaderIsTeam2WhenScore2Higher()
        {
            var (winnerIsTeam1, points) = CumulativeScoreCalculator.Contribution(100, 5_100, CumulativeScoreCalculator.Regular);
            Assert.That(winnerIsTeam1, Is.False);
            Assert.That(points, Is.EqualTo(5_000));
        }

        // ---- IsDecided ----

        [Test]
        public void TestDecidedWhenNoMapsRemain()
        {
            Assert.That(CumulativeScoreCalculator.IsDecided(0, 0, mapsPlayed: 7, picksCount: 7), Is.True);
        }

        [Test]
        public void TestDecidedAtUnrecoverableLeadBoundary()
        {
            // x = 1, threshold = 25000, lead = 25000 -> decided
            Assert.That(CumulativeScoreCalculator.IsDecided(25_000, 0, mapsPlayed: 6, picksCount: 7), Is.True);
        }

        [Test]
        public void TestNotDecidedJustBelowBoundary()
        {
            Assert.That(CumulativeScoreCalculator.IsDecided(24_999, 0, mapsPlayed: 6, picksCount: 7), Is.False);
        }

        [Test]
        public void TestNotDecidedNearTieEarly()
        {
            // x = 4, threshold = 100000, lead = 10000 -> not decided
            Assert.That(CumulativeScoreCalculator.IsDecided(10_000, 0, mapsPlayed: 3, picksCount: 7), Is.False);
        }

        [Test]
        public void TestDecidedByLargeEarlyLead()
        {
            // x = 4, threshold = 100000, lead = 100000 -> decided
            Assert.That(CumulativeScoreCalculator.IsDecided(100_000, 0, mapsPlayed: 3, picksCount: 7), Is.True);
        }
    }
}
