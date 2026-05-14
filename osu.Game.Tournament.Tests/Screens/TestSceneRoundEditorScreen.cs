// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Models;
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
        public void TestModParametersJsonRoundTrip()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("{\"DT\":{\"speed_change\":1.5},\"MOD\":{\"flag\":true},\"Key\":{\"note\":\"hello\"}}");

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
        public void TestModParametersMalformedJsonReturnsEmpty()
        {
            var parsed = RoundEditorScreen.RoundRow.RoundBeatmapEditor.RoundBeatmapRow
                .parseModParameters("{not valid json");
            Assert.That(parsed, Is.Empty);
        }

        [Test]
        public void TestSlotNameAutoComputeFromMods()
        {
            // Beatmaps seeded with Mods set but SlotName left at default empty — mirrors a
            // bracket.json authored before the auto-compute hook was ported. The RoundRow's
            // LoadComplete subscription should re-materialise SlotNames on first render.
            AddStep("seed round with empty SlotNames", () =>
            {
                Ladder.Rounds.Clear();
                var round = new TournamentRound { Name = { Value = "Auto Slot Test" } };
                round.Beatmaps.Add(new RoundBeatmap { ID = 100, Mods = "NM" });
                round.Beatmaps.Add(new RoundBeatmap { ID = 101, Mods = "NM" });
                round.Beatmaps.Add(new RoundBeatmap { ID = 102, Mods = "HD" });
                round.Beatmaps.Add(new RoundBeatmap { ID = 103, Mods = "HD" });
                round.Beatmaps.Add(new RoundBeatmap { ID = 104, Mods = "TB" });
                Ladder.Rounds.Add(round);
            });

            AddUntilStep("SlotNames auto-computed", () =>
            {
                var round = Ladder.Rounds.LastOrDefault();
                if (round == null || round.Beatmaps.Count != 5) return false;
                return round.Beatmaps[0].SlotName == "NM1"
                       && round.Beatmaps[1].SlotName == "NM2"
                       && round.Beatmaps[2].SlotName == "HD1"
                       && round.Beatmaps[3].SlotName == "HD2"
                       && round.Beatmaps[4].SlotName == "TB";
            });
        }
    }
}
