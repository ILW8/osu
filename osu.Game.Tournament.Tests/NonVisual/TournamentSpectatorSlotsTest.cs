// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class TournamentSpectatorSlotsTest
    {
        [Test]
        public void Snapshot_skipsNonParticipating_andAssignsSequentially()
        {
            var slots = TournamentSpectatorScreen.SnapshotSlots(new[]
            {
                (10, MultiplayerUserState.Spectating), // the tourney client itself — skipped
                (11, MultiplayerUserState.Playing), // slot 0
                (12, MultiplayerUserState.Idle), // skipped
                (13, MultiplayerUserState.Loaded), // slot 1
                (14, MultiplayerUserState.WaitingForLoad), // slot 2
            });

            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(slots[11], Is.EqualTo(0));
            Assert.That(slots[13], Is.EqualTo(1));
            Assert.That(slots[14], Is.EqualTo(2));
            Assert.That(slots.ContainsKey(10), Is.False);
            Assert.That(slots.ContainsKey(12), Is.False);
        }

        [Test]
        public void IsParticipating_onlyForActiveGameplayStates()
        {
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Playing), Is.True);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Loaded), Is.True);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.ReadyForGameplay), Is.True);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.WaitingForLoad), Is.True);

            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Idle), Is.False);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Ready), Is.False);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Spectating), Is.False);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.Results), Is.False);
            Assert.That(TournamentSpectatorScreen.IsParticipating(MultiplayerUserState.FinishedPlay), Is.False);
        }
    }
}
