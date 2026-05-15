// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Screens;

namespace osu.Game.Tournament.Tests
{
    public partial class TestSceneTournamentSceneManagerMultiplayer : TournamentTestScene
    {
        protected override MatchIPCInfo CreateIPCInfo() => new MultiplayerMatchIPCInfo();

        [BackgroundDependencyLoader]
        private void load()
        {
            // Mirror TournamentGameBase's production caching so any future
            // [Resolved] MultiplayerMatchIPCInfo consumer also resolves correctly.
            Dependencies.CacheAs((MultiplayerMatchIPCInfo)IPCInfo);

            Add(new TournamentSceneManager());
        }

        [Test]
        public void TestLeftColumnHostsMultiplayerControls()
        {
            // Left-column controls are the ones whose ancestor chain does NOT pass through
            // any TournamentScreen. SetupScreen and GameplayScreen also render their own
            // copies today; scoping by "not inside a screen" isolates the left-column count
            // and keeps this assertion stable across Task 4 (which removes the gameplay copy).
            AddAssert("left column hosts exactly one MultiplayerRoomConnectionControls",
                () => this.ChildrenOfType<MultiplayerRoomConnectionControls>()
                          .Count(c => !isInsideAnyTournamentScreen(c)),
                () => Is.EqualTo(1));
        }

        private static bool isInsideAnyTournamentScreen(Drawable drawable)
        {
            for (Drawable? p = drawable.Parent; p != null; p = p.Parent)
            {
                if (p is TournamentScreen)
                    return true;
            }

            return false;
        }
    }
}
