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

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            // Tests in this fixture share a single IPC instance; reset connection
            // state so each test starts disconnected and observes a clean transition.
            AddStep("reset IPC connection", () => ((MultiplayerMatchIPCInfo)IPCInfo).SetConnectedForTesting(false));
        }

        [Test]
        public void TestLeftColumnHostsMultiplayerControls()
        {
            // Left-column controls are the ones whose ancestor chain does NOT pass through
            // any TournamentScreen. SetupScreen still renders its own in-content copy by
            // design, so scoping by "not inside a screen" isolates the left-column count.
            AddAssert("left column hosts exactly one MultiplayerRoomConnectionControls",
                () => this.ChildrenOfType<MultiplayerRoomConnectionControls>()
                          .Count(c => !isInsideAnyTournamentScreen(c)),
                () => Is.EqualTo(1));
        }

        [Test]
        public void TestConnectingToIdleRoomDrivesStateToIdle()
        {
            // MapPoolScreen mirrors the chatToggle's "show chat" path by routing through
            // ipc.State on connect. Verify the IPC state lands on Idle (which downstream
            // triggers chat.Expand() via GameplayScreen's existing State binding).
            AddStep("seed non-Idle state", () =>
                ((MultiplayerMatchIPCInfo)IPCInfo).State.Value = TourneyState.Playing);

            AddStep("connect (no gameplay running)", () =>
                ((MultiplayerMatchIPCInfo)IPCInfo).SetConnectedForTesting(true, roomId: 12345));

            AddAssert("ipc state becomes Idle",
                () => ((MultiplayerMatchIPCInfo)IPCInfo).State.Value,
                () => Is.EqualTo(TourneyState.Idle));
        }

        [Test]
        public void TestConnectingDuringGameplayPreservesIpcState()
        {
            // When a room is already mid-match at the time of joining, don't override the
            // gameplay-driven chat state — leave State alone so the existing gameplay flow
            // can manage chat visibility.
            AddStep("seed non-Idle state", () =>
                ((MultiplayerMatchIPCInfo)IPCInfo).State.Value = TourneyState.Playing);

            AddStep("connect (gameplay already running)", () =>
                ((MultiplayerMatchIPCInfo)IPCInfo).SetConnectedForTesting(true, roomId: 12345, joinedDuringGameplay: true));

            AddAssert("ipc state preserved",
                () => ((MultiplayerMatchIPCInfo)IPCInfo).State.Value,
                () => Is.EqualTo(TourneyState.Playing));
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
