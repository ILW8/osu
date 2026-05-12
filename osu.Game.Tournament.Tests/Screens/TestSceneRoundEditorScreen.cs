// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Screens.Editors;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneRoundEditorScreen : TournamentScreenTestScene
    {
        private RoundEditorScreen editor = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(editor = new RoundEditorScreen
            {
                Width = 0.85f // create room for control panel
            });
        }

        [Test]
        public void TestProtectFields()
        {
            // Editor renders one RoundRow per round in LadderInfo.Rounds; the test fixture seeds at
            // least one round (see TournamentScreenTestScene / TournamentTestScene). The new fields
            // live on the same RoundRow as # of Bans.
            AddUntilStep("protect-count slider present", () =>
                editor.ChildrenOfType<SettingsSlider<int>>().Any(s => s.LabelText == "# of Protects"));

            AddUntilStep("allow-opponent-pick checkbox present", () =>
                editor.ChildrenOfType<SettingsCheckbox>()
                      .Any(c => c.LabelText == "Allow picking opponent's protects"));

            AddStep("set ProtectCount on first round to 1", () =>
            {
                Ladder.Rounds.First().ProtectCount.Value = 1;
            });
            AddAssert("ProtectCount bindable is 1", () => Ladder.Rounds.First().ProtectCount.Value == 1);

            AddStep("set AllowPickingOpponentProtects to false on first round", () =>
            {
                Ladder.Rounds.First().AllowPickingOpponentProtects.Value = false;
            });
            AddAssert("bindable is false", () => !Ladder.Rounds.First().AllowPickingOpponentProtects.Value);
        }

        [Test]
        public void TestModParametersFreeForm()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("DT.speed_change=1.5\nMOD.flag=true\nKey.note=hello");

            Assert.That(parsed["DT"]["speed_change"], Is.EqualTo(1.5));
            Assert.That(parsed["MOD"]["flag"], Is.EqualTo(true));
            Assert.That(parsed["Key"]["note"], Is.EqualTo("hello"));
        }

        [Test]
        public void TestModParametersEmptyInput()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters(string.Empty);
            Assert.That(parsed, Is.Empty);
        }

        [Test]
        public void TestModParametersMalformedLinesSkipped()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("=1.5\nDT.\nfoo=bar\nDT.x=2.0");

            Assert.That(parsed.Count, Is.EqualTo(1));
            Assert.That(parsed["DT"]["x"], Is.EqualTo(2.0));
        }
    }
}
