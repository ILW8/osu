// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.MapPool;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneMapPoolScreen : TournamentScreenTestScene
    {
        private MapPoolScreen screen = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(screen = new TestMapPoolScreen { Width = 0.7f });
        }

        [SetUpSteps]
        public override void SetUpSteps()
        {
            AddStep("reset state", resetState);
        }

        private void resetState()
        {
            Ladder.SplitMapPoolByMods.Value = true;

            Ladder.CurrentMatch.Value = new TournamentMatch();
            Ladder.Matches.First().PicksBans.Clear();
            Ladder.Matches.First().Protects.Clear();
            Ladder.Matches.First().Sets.Clear();
            Ladder.CurrentMatch.Value = Ladder.Matches.First();
        }

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
        });

        [Test]
        public void TestFewMaps()
        {
            AddStep("load few maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 8; i++)
                    addBeatmap();
            });

            AddStep("reset match", () =>
            {
                Ladder.CurrentMatch.Value = new TournamentMatch();
            });

            assertTwoWide();
        }

        [Test]
        public void TestJustEnoughMaps()
        {
            AddStep("load just enough maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 18; i++)
                    addBeatmap();
            });

            AddStep("reset state", resetState);

            assertTwoWide();
        }

        [Test]
        public void TestManyMaps()
        {
            AddStep("load many maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 19; i++)
                    addBeatmap();
            });

            AddStep("reset state", resetState);

            assertThreeWide();
        }

        [Test]
        public void TestJustEnoughMods()
        {
            AddStep("load many maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 11; i++)
                    addBeatmap(i > 4 ? Ruleset.Value.CreateInstance().AllMods.ElementAt(i).Acronym : "NM");
            });

            AddStep("reset state", resetState);

            assertTwoWide();
        }

        private void assertTwoWide() =>
            AddAssert("ensure layout width is 2", () => screen.ChildrenOfType<FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>>().First().Padding.Left > 0);

        private void assertThreeWide() =>
            AddAssert("ensure layout width is 3", () => screen.ChildrenOfType<FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>>().First().Padding.Left == 0);

        [Test]
        public void TestManyMods()
        {
            AddStep("load many maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 12; i++)
                    addBeatmap(i > 4 ? Ruleset.Value.CreateInstance().AllMods.ElementAt(i).Acronym : "NM");
            });

            AddStep("reset state", resetState);

            assertThreeWide();
        }

        [Test]
        public void TestSplitMapPoolByMods()
        {
            AddStep("load many maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 12; i++)
                    addBeatmap(i > 4 ? Ruleset.Value.CreateInstance().AllMods.ElementAt(i).Acronym : "NM");
            });

            AddStep("disable splitting map pool by mods", () => Ladder.SplitMapPoolByMods.Value = false);

            AddStep("reset state", resetState);
        }

        [Test]
        public void TestTiebreakerSetDisplay()
        {
            int originalTiebreakerSetIndex = screen.TiebreakerSetIndex;

            AddStep("load first weekend maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 4; i++)
                    addBeatmap("NM", $"NM map #{i}");

                resetState();
            });

            AddStep("disable cumulative score", () => Ladder.CumulativeScore.Value = false);
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);

            AddStep("Set first set to be a tiebreaker set", () => screen.TiebreakerSetIndex = 0);

            AddStep("pick nm1", () =>
            {
                screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Red Pick").TriggerClick();
                clickBeatmapPanel(0);
            });
            AddStep("Reset tiebreaker set index", () => screen.TiebreakerSetIndex = originalTiebreakerSetIndex);
            AddStep("update current beatmap", () =>
            {
                var newTournamentBeatmap = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.First(b => screen.ChildrenOfType<TournamentBeatmapPanel>().ElementAt(0).Beatmap!.OnlineID == b.Beatmap!.OnlineID).Beatmap;
                IPCInfo.Beatmap.Value = newTournamentBeatmap;
            });
            AddStep("set scores on nm1", () => Ladder.CurrentMatch.Value!.MapScores["NM1"] = new Tuple<long, long>(Random.Shared.Next() % 1_000_000, Random.Shared.Next() % 1_000_000));

            AddStep("set blue pick", () => screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Pick").TriggerClick());
            AddStep("pick nm2", () => clickBeatmapPanel(1));
            AddStep("set scores on nm2", () => Ladder.CurrentMatch.Value!.MapScores["NM2"] = new Tuple<long, long>(Random.Shared.Next() % 1_000_000, Random.Shared.Next() % 1_000_000));

            AddStep("set red pick", () => screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Pick").TriggerClick());
            AddStep("pick nm3", () => clickBeatmapPanel(2));
            AddStep("set scores on nm3", () => Ladder.CurrentMatch.Value!.MapScores["NM3"] = new Tuple<long, long>(Random.Shared.Next() % 1_000_000, Random.Shared.Next() % 1_000_000));

            // The first set was created when TiebreakerSetIndex=0 was active, so it must remain marked as a tiebreaker
            // even after the index was reset. Subsequent picks fall into regular sets.
            AddAssert("first set is tiebreaker", () => Ladder.CurrentMatch.Value!.Sets[0].IsTiebreaker, () => Is.True);
            AddUntilStep("first set panel is tiebreaker",
                () => screen.ChildrenOfType<TournamentSetPanel>().FirstOrDefault()?.Model.IsTiebreaker == true);
        }

        [Test]
        public void TestLgaSetScoring()
        {
            AddStep("load first weekend maps", () =>
            {
                Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Clear();

                for (int i = 0; i < 4; i++)
                    addBeatmap("NM", $"NM map #{i}");
                for (int i = 0; i < 2; i++)
                    addBeatmap("HD", $"HD map #{i}");
                for (int i = 0; i < 2; i++)
                    addBeatmap("HR", $"HR map #{i}");
                for (int i = 0; i < 3; i++)
                    addBeatmap("DT", $"DT map #{i}");

                resetState();
            });

            AddStep("disable cumulative score", () => Ladder.CumulativeScore.Value = false);
            AddStep("enable cumulative score", () => Ladder.CumulativeScore.Value = true);

            // hardcoded bans for now oh well
            AddStep("ban map 1", () =>
            {
                screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Ban").TriggerClick();
                clickBeatmapPanel(2);
            });
            AddStep("ban map 2", () =>
            {
                screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Red Ban").TriggerClick();
                clickBeatmapPanel(3);
            });
            AddStep("ban map 3", () =>
            {
                screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Ban").TriggerClick();
                clickBeatmapPanel(4);
            });
            AddStep("ban map 4", () =>
            {
                screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Red Ban").TriggerClick();
                clickBeatmapPanel(5);
            });

            AddStep("set red pick", () => screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Red Pick").TriggerClick());
            AddStep("pick nm1", () => clickBeatmapPanel(0));
            AddStep("update current beatmap", () =>
            {
                var newTournamentBeatmap = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.First(b => screen.ChildrenOfType<TournamentBeatmapPanel>().ElementAt(0).Beatmap!.OnlineID == b.Beatmap!.OnlineID).Beatmap;
                IPCInfo.Beatmap.Value = newTournamentBeatmap;
            });
            AddStep("set scores on nm1", () => Ladder.CurrentMatch.Value!.MapScores["NM1"] = new Tuple<long, long>(Random.Shared.Next() % 1_000_000, Random.Shared.Next() % 1_000_000));

            AddStep("set blue pick", () => screen.ChildrenOfType<TourneyButton>().First(btn => btn.Text == "Blue Pick").TriggerClick());
            AddStep("pick nm2", () => clickBeatmapPanel(1));
            // Hardcode scores so we can assert the winner deterministically: blue takes the set 1.5M to 1.0M cumulative.
            AddStep("set scores on nm1", () => Ladder.CurrentMatch.Value!.MapScores["NM1"] = new Tuple<long, long>(500_000, 750_000));
            AddStep("set scores on nm2", () => Ladder.CurrentMatch.Value!.MapScores["NM2"] = new Tuple<long, long>(500_000, 750_000));

            AddAssert("one set tracked", () => Ladder.CurrentMatch.Value!.Sets, () => Has.Count.EqualTo(1));
            AddAssert("set is not tiebreaker", () => Ladder.CurrentMatch.Value!.Sets[0].IsTiebreaker, () => Is.False);
            AddAssert("set has both maps populated",
                () => Ladder.CurrentMatch.Value!.Sets[0].Map1Id.Value != 0 && Ladder.CurrentMatch.Value!.Sets[0].Map2Id.Value != 0,
                () => Is.True);
            AddUntilStep("one TournamentSetPanel rendered",
                () => screen.ChildrenOfType<TournamentSetPanel>().Count() == 1);
            AddUntilStep("set panel awarded to blue",
                () => screen.ChildrenOfType<TournamentSetPanel>().FirstOrDefault()?.Winner == TeamColour.Blue);
        }

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
        }

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

            AddStep("right-click map 0", () => rightClickBeatmapPanel(0));
            AddAssert("pick removed", () => Ladder.CurrentMatch.Value!.PicksBans, () => Has.Count.EqualTo(0));
            AddAssert("protect still present", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(1));

            AddStep("right-click map 0 again", () => rightClickBeatmapPanel(0));
            AddAssert("protect removed", () => Ladder.CurrentMatch.Value!.Protects, () => Has.Count.EqualTo(0));
        }

        private void addBeatmap(string mods = "NM", string? titleOverride = null)
        {
            var newBeatmap = CreateSampleBeatmap(titleOverride);

            int modSlotIndex = Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Count(bm => bm.Mods == mods) + 1;

            Ladder.CurrentMatch.Value!.Round.Value!.Beatmaps.Add(new RoundBeatmap
            {
                Beatmap = newBeatmap,
                ID = newBeatmap.OnlineID,
                Mods = mods,
                SlotName = $"{mods}{modSlotIndex}"
            });
        }

        private void clickBeatmapPanel(int index)
        {
            InputManager.MoveMouseTo(screen.ChildrenOfType<TournamentBeatmapPanel>().ElementAt(index));
            InputManager.Click(MouseButton.Left);
        }

        private void rightClickBeatmapPanel(int index)
        {
            InputManager.MoveMouseTo(screen.ChildrenOfType<TournamentBeatmapPanel>().ElementAt(index));
            InputManager.Click(MouseButton.Right);
        }

        private partial class TestMapPoolScreen : MapPoolScreen
        {
            // this is a bit of a test-specific workaround.
            // the way pick/ban is implemented is a bit funky; the screen itself is what handles the mouse there,
            // rather than the beatmap panels themselves.
            // in some extreme situations headless it may turn out that the panels overflow the screen,
            // and as such picking stops working anymore outside of the bounds of the screen drawable.
            // this override makes it so the screen sees all of the input at all times, making that impossible to happen.
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;
        }
    }
}
