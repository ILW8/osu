// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
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
        public void TestCumulativeScoreContribution()
        {
            AddStep("enable cumulative scoring", () => Ladder.CumulativeScore.Value = true);
            AddStep("configure round and ipc", () =>
            {
                var match = Ladder.CurrentMatch.Value!;
                match.Round.Value!.PicksCount.Value = 7;
                match.Round.Value!.Beatmaps.Clear();
                match.Round.Value!.Beatmaps.Add(new RoundBeatmap { ID = 1001, Mods = string.Empty });
                match.PicksBans.Clear();
                match.PicksBans.Add(new BeatmapChoice { BeatmapID = 1001, Team = TeamColour.Red, Type = ChoiceType.Pick });
                match.Team1Score.Value = 0;
                match.Team2Score.Value = 0;
                match.Completed.Value = false;

                IPCInfo.Beatmap.Value = new TournamentBeatmap { OnlineID = 1001 };
                IPCInfo.Score1.Value = 100_000;
                IPCInfo.Score2.Value = 0;
                IPCInfo.State.Value = TourneyState.Idle;
            });

            createScreen();

            // warmup defaults on for a 0-0 match; turn it off so the result is scored.
            toggleWarmup();

            // step through the realistic IPC flow (Playing then Ranking); transitioning
            // straight from Idle to Ranking is intercepted by the chat-toggle state binding.
            AddStep("enter playing", () => IPCInfo.State.Value = TourneyState.Playing);
            AddStep("enter ranking", () => IPCInfo.State.Value = TourneyState.Ranking);

            AddAssert("team1 gained capped regular contribution",
                () => Ladder.CurrentMatch.Value!.Team1Score.Value == 10_000);
            AddAssert("match not yet decided",
                () => Ladder.CurrentMatch.Value!.Completed.Value == false);

            // the match/ladder are shared cached instances; restore defaults so this test
            // does not pollute the warmup-on heuristic (Team1Score + Team2Score == 0) of sibling tests.
            AddStep("restore shared state", () =>
            {
                Ladder.CumulativeScore.Value = true;
                var match = Ladder.CurrentMatch.Value!;
                match.Team1Score.Value = 0;
                match.Team2Score.Value = 0;
                match.Completed.Value = false;
                IPCInfo.State.Value = TourneyState.Idle;
            });
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
