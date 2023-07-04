// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.MapPool;

namespace osu.Game.Tournament.Tests
{
    public partial class TestSceneTournamentSceneManager : TournamentTestScene
    {
        private readonly TournamentSceneManager tsm = new TournamentSceneManager();

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(tsm);
        }

        [Test]
        public void TestScreenSwitch()
        {
            AddStep("set screen to gameplay", () => tsm.SetScreen(typeof(GameplayScreen)));
            AddWaitStep("wait for some time", 5);
            AddStep("set screen to mappool", () => tsm.SetScreen(typeof(MapPoolScreen)));
            AddWaitStep("wait for some time", 5);
            AddStep("set screen to gameplay", () => tsm.SetScreen(typeof(GameplayScreen)));
        }
    }
}
