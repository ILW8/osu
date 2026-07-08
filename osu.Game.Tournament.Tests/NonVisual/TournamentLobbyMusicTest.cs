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
            Assert.That(TournamentLobbyMusic.Decide(isConnected: false, TourneyState.Idle, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsWhenBeatmapUnresolved()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: false, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void PlaysWhenIdleAndConnectedWithBeatmap()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Play));
        }

        [Test]
        public void ContinueOnlyDuringWaitingForClients()
        {
            // WaitingForClients is a bridge: continue if already playing (came from Idle), but
            // never start. Starting here is the production bug that causes the next-round song
            // to play during the post-Ranking window when the host advances quickly.
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.WaitingForClients, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.ContinueOnly));
        }

        [Test]
        public void StopsDuringPlaying()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Playing, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsDuringRanking()
        {
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Ranking, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsDuringInitialising()
        {
            // Initialising is never a "lobby" state — be conservative and don't play.
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Initialising, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: false),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }

        [Test]
        public void StopsWhenPlayersActiveEvenInLobbyState()
        {
            // The shared cached beatmap track is driven by the spectator master clock while any player
            // is playing; lobby music must not run (or re-arm Track.Looping) then, even if TourneyState
            // still reads Idle/WaitingForClients because GameplayStarted was missed. Guards the
            // intermittent "seek back to start at map end" loop.
            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));

            Assert.That(TournamentLobbyMusic.Decide(isConnected: true, TourneyState.WaitingForClients, hasResolvedBeatmap: true, hasActiveSpectatorPlayers: true),
                Is.EqualTo(TournamentLobbyMusic.PlaybackAction.Stop));
        }
    }
}
