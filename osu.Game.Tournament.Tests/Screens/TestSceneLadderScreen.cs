// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Graphics.Cursor;
using osu.Game.Tournament.Screens.Ladder;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneLadderScreen : TournamentScreenTestScene
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new OsuContextMenuContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new LadderScreen()
            });
        }

        [Test]
        public void TestCumulativeScoreBypassesBestOfCompletion()
        {
            AddStep("enable cumulative scoring", () => Ladder.CumulativeScore.Value = true);
            AddStep("reset match", () =>
            {
                Ladder.CurrentMatch.Value!.Completed.Value = false;
                Ladder.CurrentMatch.Value!.Round.Value!.PicksCount.Value = 9;
                Ladder.CurrentMatch.Value!.Team1Score.Value = 0;
                Ladder.CurrentMatch.Value!.Team2Score.Value = 0;
            });

            // 5 > 9/2 -> would auto-complete a best-of match.
            AddStep("set win-count-like score", () => Ladder.CurrentMatch.Value!.Team1Score.Value = 5);
            AddAssert("not completed in cumulative mode", () => Ladder.CurrentMatch.Value!.Completed.Value == false);

            AddStep("disable cumulative scoring", () => Ladder.CumulativeScore.Value = false);
            // nudge a score so updateWinConditions runs again under best-of rules.
            AddStep("nudge score", () => Ladder.CurrentMatch.Value!.Team1Score.Value = 6);
            AddAssert("completed in best-of mode", () => Ladder.CurrentMatch.Value!.Completed.Value);

            AddStep("restore cumulative scoring default", () => Ladder.CumulativeScore.Value = true);
        }
    }
}
