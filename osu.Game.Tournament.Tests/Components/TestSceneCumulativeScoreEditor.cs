// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneCumulativeScoreEditor : TournamentTestScene
    {
        // SwitchButton resolves this; the real screens (TournamentScreen) cache it, the bare test scene does not.
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        private CumulativeScoreEditor editor = null!;

        public TestSceneCumulativeScoreEditor()
        {
            Add(editor = new CumulativeScoreEditor
            {
                Width = 300,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });
        }

        private SettingsNumberBox team1Box => editor.ChildrenOfType<SettingsNumberBox>().First();
        private SettingsNumberBox team2Box => editor.ChildrenOfType<SettingsNumberBox>().Last();
        private LabelledSwitchButton completedSwitch => editor.ChildrenOfType<LabelledSwitchButton>().Single();

        [Test]
        public void TestVisibilityFollowsCumulativeMode()
        {
            AddStep("enable cumulative", () => Ladder.CumulativeScore.Value = true);
            AddAssert("editor visible", () => editor.Alpha == 1);
            AddStep("disable cumulative", () => Ladder.CumulativeScore.Value = false);
            AddAssert("editor hidden", () => editor.Alpha == 0);
            AddStep("clear current match", () => Ladder.CurrentMatch.Value = null);
            AddStep("re-enable cumulative", () => Ladder.CumulativeScore.Value = true);
            AddAssert("hidden without match", () => editor.Alpha == 0);
        }

        [Test]
        public void TestTwoWayScoreBinding()
        {
            AddStep("enable cumulative", () => Ladder.CumulativeScore.Value = true);
            AddStep("set model score", () => Ladder.CurrentMatch.Value!.Team1Score.Value = 1234);
            AddAssert("box reflects model", () => team1Box.Current.Value == 1234);
            AddStep("write via box", () => team2Box.Current.Value = 50);
            AddAssert("model reflects box", () => Ladder.CurrentMatch.Value!.Team2Score.Value == 50);
        }

        [Test]
        public void TestRebindsOnMatchChange()
        {
            AddStep("enable cumulative", () => Ladder.CumulativeScore.Value = true);
            AddStep("set score on current", () => Ladder.CurrentMatch.Value!.Team1Score.Value = 10);
            AddStep("switch to new match", () =>
            {
                var other = CreateSampleMatch();
                other.Team1Score.Value = 999;
                Ladder.Matches.Add(other);
                Ladder.CurrentMatch.Value = other;
            });
            AddAssert("box reflects new match", () => team1Box.Current.Value == 999);
        }

        [Test]
        public void TestCompletedToggleBindsToMatch()
        {
            AddStep("enable cumulative", () => Ladder.CumulativeScore.Value = true);
            AddStep("set model completed", () => Ladder.CurrentMatch.Value!.Completed.Value = true);
            AddAssert("switch reflects model", () => completedSwitch.Current.Value);
            AddStep("toggle off via switch", () => completedSwitch.Current.Value = false);
            AddAssert("model reflects switch", () => !Ladder.CurrentMatch.Value!.Completed.Value);
        }
    }
}
