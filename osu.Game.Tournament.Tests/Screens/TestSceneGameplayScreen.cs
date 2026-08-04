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
        public void TestMatchScoreIncrementsOnceOnMapCompletion()
        {
            AddStep("set state to idle", () => IPCInfo.State.Value = TourneyState.Idle);

            createScreen();

            toggleWarmup();

            AddStep("team 1 wins the map", () =>
            {
                IPCInfo.Score1.Value = 500000;
                IPCInfo.Score2.Value = 400000;
            });

            AddStep("begin gameplay", () => IPCInfo.State.Value = TourneyState.Playing);
            AddStep("finish map", () => IPCInfo.State.Value = TourneyState.Ranking);
            AddAssert("team 1 awarded one point", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(1));

            AddStep("re-show screen", () => this.ChildrenOfType<GameplayScreen>().Single().Show());
            AddAssert("team 1 still has one point", () => Ladder.CurrentMatch.Value!.Team1Score.Value, () => Is.EqualTo(1));

            AddStep("reset match score", () => Ladder.CurrentMatch.Value!.Team1Score.Value = 0);
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
