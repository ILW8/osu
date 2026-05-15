// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class TournamentLobbyMusicTest
    {
        [Test]
        public void DoesNotPlayWhenDisconnected()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: false, TourneyState.Idle, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void DoesNotPlayWhenBeatmapUnresolved()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: false), Is.False);
        }

        [Test]
        public void PlaysWhenIdleAndConnectedWithBeatmap()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: true), Is.True);
        }

        [Test]
        public void PlaysWhenWaitingForClients()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.WaitingForClients, hasResolvedBeatmap: true), Is.True);
        }

        [Test]
        public void DoesNotPlayDuringPlaying()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Playing, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void DoesNotPlayDuringRanking()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Ranking, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void DoesNotPlayDuringInitialising()
        {
            // Initialising is never a "lobby" state — be conservative and don't play.
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Initialising, hasResolvedBeatmap: true), Is.False);
        }
    }
}
