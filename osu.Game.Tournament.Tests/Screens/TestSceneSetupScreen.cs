// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Tournament.Screens.Setup;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneSetupScreen : TournamentScreenTestScene
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new SetupScreen());
        }

        [Test]
        public void TestUse1V1Toggle()
        {
            AddStep("ensure off", () => Ladder.Use1V1Mode.Value = false);

            AddStep("click 1v1 switch", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(r => rowMatchesLabel(r, "1v1 mode"));
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("Use1V1Mode is true", () => Ladder.Use1V1Mode.Value, () => Is.True);

            AddStep("click again", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(r => rowMatchesLabel(r, "1v1 mode"));
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("Use1V1Mode is false", () => Ladder.Use1V1Mode.Value, () => Is.False);
        }

        [Test]
        public void TestUseCumulativeScoreToggle()
        {
            AddStep("ensure off", () => Ladder.CumulativeScore.Value = false);

            AddStep("click cumulative-score switch", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(r => rowMatchesLabel(r, "Use cumulative score"));
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("CumulativeScore is true", () => Ladder.CumulativeScore.Value, () => Is.True);

            AddStep("click again", () =>
            {
                var row = this.ChildrenOfType<LabelledSwitchButton>().First(r => rowMatchesLabel(r, "Use cumulative score"));
                row.ChildrenOfType<SwitchButton>().First().TriggerClick();
            });

            AddAssert("CumulativeScore is false", () => Ladder.CumulativeScore.Value, () => Is.False);
        }

        private static bool rowMatchesLabel(LabelledSwitchButton row, string label)
        {
            // Concatenate all SpriteText characters that belong to the label flow container (the
            // first OsuTextFlowContainer in the row hierarchy). LabelledDrawable.Label has no
            // getter so we reconstruct it from the rendered SpriteTexts.
            string text = string.Concat(row.ChildrenOfType<SpriteText>().Select(s => s.Text.ToString()));
            return text.Contains(label);
        }
    }
}
