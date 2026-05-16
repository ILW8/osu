// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Gameplay.Components;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneGameplayScreen : TournamentScreenTestScene
    {
        [Cached]
        private TournamentMatchChatDisplay chat = new TournamentMatchChatDisplay { Width = 0.5f };

        [Test]
        public void TestWarmup()
        {
            createScreen();

            checkScoreVisibility(false);

            toggleWarmup();
            checkScoreVisibility(true);

            toggleWarmup();
            checkScoreVisibility(false);
        }

        [Test]
        public void TestScoreAddCumulative()
        {
            AddStep("disable cumulative score", () => Ladder.CumulativeScore.Value = false);
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);

            createScreen();
            toggleWarmup();

            AddStep("add set with maps 1 & 2", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 1 }, Map2Id = { Value = 2 } }));
            AddStep("add set with maps 3 & 4", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 3 }, Map2Id = { Value = 4 } }));

            for (int i = 0; i < 2; i++)
            {
                int i1 = i;
                AddStep($"switch to map {i + 1}", () => IPCInfo.Beatmap.Value = new TournamentBeatmap { OnlineID = i1 + 1 });

                AddStep("set state: idle", () => IPCInfo.State.Value = TourneyState.Idle);

                AddStep("set state: playing", () => IPCInfo.State.Value = TourneyState.Playing);

                int iteration = i;
                AddStep("add score", () =>
                {
                    IPCInfo.Score1.Value = iteration == 0 ? 127_727 : 492_000;
                    IPCInfo.Score2.Value = iteration == 0 ? 63_000 : 613_727;
                });
                AddStep("set state: ranking", () => IPCInfo.State.Value = TourneyState.Ranking);

                AddWaitStep("wait a bit", 8);

                AddStep("clear scores", () =>
                {
                    IPCInfo.Score1.Value = 0;
                    IPCInfo.Score2.Value = 0;
                });
            }

            // After playing both maps in the set, cumulative scores: red 619_727 vs blue 676_727 — blue wins set.
            AddAssert("team1 set wins is 0", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(0));
            AddAssert("team2 set wins is 1", () => Ladder.CurrentMatch.Value!.Team2Score.Value, () => Is.EqualTo(1));

            AddStep("set state: idle", () => IPCInfo.State.Value = TourneyState.Idle);
            AddStep("switch to map 3", () => IPCInfo.Beatmap.Value = new TournamentBeatmap { OnlineID = 3 });
        }

        [Test]
        public void TestScoreAddCumulativeTiebreaker()
        {
            AddStep("disable cumulative score", () => Ladder.CumulativeScore.Value = false);
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);

            createScreen();
            toggleWarmup();

            AddStep("add tiebreaker set with maps 3, 4, 5", () => Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet(true) { Map1Id = { Value = 1 }, Map2Id = { Value = 2 }, Map3Id = { Value = 3 } }));

            for (int i = 0; i < 3; i++)
            {
                int i1 = i;
                AddStep($"switch to map {i + 1}", () => IPCInfo.Beatmap.Value = new TournamentBeatmap { OnlineID = i1 + 1 });

                AddStep("set state: idle", () => IPCInfo.State.Value = TourneyState.Idle);

                AddStep("set state: playing", () => IPCInfo.State.Value = TourneyState.Playing);

                int iteration = i + 1;
                AddStep("add score", () =>
                {
                    IPCInfo.Score1.Value = iteration * 1_000;
                    IPCInfo.Score2.Value = iteration;
                });
                AddStep("set state: ranking", () => IPCInfo.State.Value = TourneyState.Ranking);

                AddWaitStep("wait a bit", 8);

                AddStep("clear scores", () =>
                {
                    IPCInfo.Score1.Value = 0;
                    IPCInfo.Score2.Value = 0;
                });
            }

            // Tiebreaker set should not award a point until the third map completes; red score (1000+2000+3000) > blue (1+2+3) so red wins.
            AddAssert("team1 set wins is 1", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(1));
            AddAssert("team2 set wins is 0", () => Ladder.CurrentMatch.Value!.Team2Score.Value, () => Is.EqualTo(0));
        }

        [Test]
        public void TestScoreCumulativeDelta()
        {
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);

            createScreen();
            toggleWarmup();

            for (int i = 0; i < 7; i++)
            {
                AddStep($"add map {i + 1} results", () =>
                {
                    Ladder.CurrentMatch.Value!.Team1Score.Value += RNG.Next(1_000_000);
                    Ladder.CurrentMatch.Value!.Team2Score.Value += RNG.Next(1_000_000);
                });

                AddUntilStep("wait for score delta to settle", () =>
                {
                    var scoreDeltaDrawable = this.ChildrenOfType<MatchHeader.MatchCumulativeScoreDiffCounter>().First();
                    return Math.Abs(scoreDeltaDrawable.DisplayedCount - scoreDeltaDrawable.Current.Value) < 0.0001f;
                });
                AddRepeatStep("wait a bit more", () => { }, 8);
            }
        }

        [Test]
        public void TestMatchAutoCompleteAtPointsToWin()
        {
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);
            AddStep("set BestOf 5 (PointsToWin = 3)", () => Ladder.CurrentMatch.Value!.Round.Value!.BestOf.Value = 5);
            AddStep("ensure map 6 in round", () =>
            {
                if (Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.All(b => b.ID != 6))
                    Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Add(new RoundBeatmap { ID = 6, SlotName = "HR2" });
            });
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

            // Teardown: restore fixture defaults so subsequent tests start from a clean round.
            AddStep("teardown — clear Sets", () => Ladder.CurrentMatch.Value!.Sets.Clear());
            AddStep("teardown — restore BestOf default", () => Ladder.CurrentMatch.Value!.Round.Value!.BestOf.Value = 9);
            AddStep("teardown — remove map 6 from round", () =>
            {
                var round = Ladder.CurrentMatch.Value!.Round.Value!;
                var extra = round.Beatmaps.FirstOrDefault(b => b.ID == 6);
                if (extra != null)
                    round.Beatmaps.Remove(extra);
            });
            AddStep("teardown — reset Completed", () => Ladder.CurrentMatch.Value!.Completed.Value = false);
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

        [Test]
        public void TestCumulativeScoreCounterPerTeam()
        {
            // Per-team cumulative counters follow Sets.LastOrDefault() (the set the latest pick landed into),
            // not ipc.Beatmap. This lets the stream operator override the displayed set by editing picks,
            // independent of what the multiplayer room is currently showing.
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);
            AddStep("seed two sets (NM1+NM2, HD1+HD2)", () =>
            {
                Ladder.CurrentMatch.Value!.Sets.Clear();
                Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 1 }, Map2Id = { Value = 2 } });
                Ladder.CurrentMatch.Value!.Sets.Add(new MatchSet { Map1Id = { Value = 3 }, Map2Id = { Value = 4 } });
            });
            AddStep("seed MapScores for both sets", () =>
            {
                Ladder.CurrentMatch.Value!.MapScores["NM1"] = new Tuple<long, long>(100_000, 50_000);
                Ladder.CurrentMatch.Value!.MapScores["NM2"] = new Tuple<long, long>(200_000, 300_000);
                Ladder.CurrentMatch.Value!.MapScores["HD1"] = new Tuple<long, long>(400_000, 250_000);
                Ladder.CurrentMatch.Value!.MapScores["HD2"] = new Tuple<long, long>(150_000, 350_000);
            });

            createScreen();
            toggleWarmup();

            // Last set is set 2 (HD1+HD2). Red sum: 400+150 = 550k. Blue sum: 250+350 = 600k.
            AddUntilStep("red counter shows 550k (set 2's red sum)", () =>
            {
                var redCounter = this.ChildrenOfType<TeamDisplay.MatchCumulativeScoreCounter>().FirstOrDefault(c => c.TeamColour == TeamColour.Red);
                return redCounter != null && Math.Abs(redCounter.Current.Value - 550_000) < 0.5;
            });
            AddUntilStep("blue counter shows 600k (set 2's blue sum)", () =>
            {
                var blueCounter = this.ChildrenOfType<TeamDisplay.MatchCumulativeScoreCounter>().FirstOrDefault(c => c.TeamColour == TeamColour.Blue);
                return blueCounter != null && Math.Abs(blueCounter.Current.Value - 600_000) < 0.5;
            });

            // Remove set 2 — counters should snap to set 1's sums even though ipc.Beatmap is untouched.
            AddStep("remove set 2", () => Ladder.CurrentMatch.Value!.Sets.RemoveAt(1));

            AddUntilStep("red counter shows 300k (set 1's red sum)", () =>
            {
                var redCounter = this.ChildrenOfType<TeamDisplay.MatchCumulativeScoreCounter>().FirstOrDefault(c => c.TeamColour == TeamColour.Red);
                return redCounter != null && Math.Abs(redCounter.Current.Value - 300_000) < 0.5;
            });
            AddUntilStep("blue counter shows 350k (set 1's blue sum)", () =>
            {
                var blueCounter = this.ChildrenOfType<TeamDisplay.MatchCumulativeScoreCounter>().FirstOrDefault(c => c.TeamColour == TeamColour.Blue);
                return blueCounter != null && Math.Abs(blueCounter.Current.Value - 350_000) < 0.5;
            });

            AddStep("teardown — clear Sets", () => Ladder.CurrentMatch.Value!.Sets.Clear());
            AddStep("teardown — clear MapScores", () => Ladder.CurrentMatch.Value!.MapScores.Clear());
        }

        [Test]
        public void TestStartupState([Values] TourneyState state)
        {
            AddStep("set state", () => IPCInfo.State.Value = state);
            createScreen();
        }

        [Test]
        public void TestStartupStateNoCurrentMatch([Values] TourneyState state)
        {
            AddStep("set null current", () => Ladder.CurrentMatch.Value = null);
            AddStep("set state", () => IPCInfo.State.Value = state);
            createScreen();
        }

        private void createScreen()
        {
            AddStep("setup screen", () =>
            {
                Remove(chat, false);

                Children = new Drawable[]
                {
                    new GameplayScreen(),
                    chat,
                };
            });
        }

        private void checkScoreVisibility(bool visible)
            => AddUntilStep($"scores {(visible ? "shown" : "hidden")}",
                () => this.ChildrenOfType<TeamScore>().All(score => score.Alpha == (visible ? 1 : 0)));

        private void toggleWarmup()
            => AddStep("toggle warmup", () => this.ChildrenOfType<LabelledSwitchButton>().First().ChildrenOfType<SwitchButton>().First().TriggerClick());
    }
}
