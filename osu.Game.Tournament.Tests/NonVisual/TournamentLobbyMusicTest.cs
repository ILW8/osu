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
        public void StopsWhenDisconnected()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: false, TourneyState.Idle, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsWhenBeatmapUnresolved()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void PlaysWhenIdleAndConnectedWithBeatmap()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Play));
        }

        [Test]
        public void ContinueOnlyDuringWaitingForClients()
        {
            // WaitingForClients is a bridge: continue if already playing (came from Idle), but
            // never start. Starting here is the production bug that causes the next-round song
            // to play during the post-Ranking window when the host advances quickly.
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.WaitingForClients, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.ContinueOnly));
        }

        [Test]
        public void StopsDuringPlaying()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Playing, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsDuringRanking()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Ranking, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsDuringInitialising()
        {
            // Initialising is never a "lobby" state — be conservative and don't play.
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Initialising, hasResolvedBeatmap: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }
    }
}
