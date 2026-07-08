// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class MultiplayerMatchStateTest
    {
        [Test]
        public void IdleWhenEmpty()
        {
            Assert.That(MultiplayerMatchIPCInfo.DeriveState(Array.Empty<MultiplayerUserState>()),
                Is.EqualTo(TourneyState.Idle));
        }

        [Test]
        public void IdleWhenNobodyParticipating()
        {
            Assert.That(MultiplayerMatchIPCInfo.DeriveState(new[]
            {
                MultiplayerUserState.Idle,
                MultiplayerUserState.Ready,
                MultiplayerUserState.Results,
            }), Is.EqualTo(TourneyState.Idle));
        }

        [Test]
        public void PlayingWhenAnyPlaying()
        {
            Assert.That(MultiplayerMatchIPCInfo.DeriveState(new[]
            {
                MultiplayerUserState.Idle,
                MultiplayerUserState.Playing,
                MultiplayerUserState.Loaded,
            }), Is.EqualTo(TourneyState.Playing));
        }

        [Test]
        public void WaitingForClientsWhenLoadingButNonePlaying()
        {
            Assert.That(MultiplayerMatchIPCInfo.DeriveState(new[]
            {
                MultiplayerUserState.Idle,
                MultiplayerUserState.WaitingForLoad,
                MultiplayerUserState.ReadyForGameplay,
            }), Is.EqualTo(TourneyState.WaitingForClients));
        }
    }
}
