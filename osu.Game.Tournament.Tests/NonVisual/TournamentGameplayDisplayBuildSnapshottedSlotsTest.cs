// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.NonVisual
{
    /// <summary>
    /// Unit tests for <see cref="TournamentGameplayDisplay.BuildSnapshottedSlots"/>, the pure
    /// projection from multiplayer room users + room name to the user→slot map used by the
    /// tournament spectator grid.
    /// </summary>
    [TestFixture]
    public class TournamentGameplayDisplayBuildSnapshottedSlotsTest
    {
        private const int max_slots = 8;

        [Test]
        public void FillsSlotsInRoomOrderWhenNoTeamsInName()
        {
            var users = new[]
            {
                user(1, "alice"),
                user(2, "bob"),
                user(3, "carol"),
            };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, "Battle Royale Round 3", max_slots);

            Assert.That(result[1], Is.EqualTo(0));
            Assert.That(result[2], Is.EqualTo(1));
            Assert.That(result[3], Is.EqualTo(2));
        }

        [Test]
        public void ReservesSlotsByTeamNameFromRoomName()
        {
            // Room name convention: "ACRONYM: (Name 1) vs (Name 2)".
            // Name 1 should land at slot 0 (left), Name 2 at slot 1 (right), regardless of
            // their position in the users list. Remaining users fill from slot 2 onward.
            var users = new[]
            {
                user(10, "carol"),
                user(20, "alice"), // expected slot 0 via room name reservation
                user(30, "dave"),
                user(40, "bob"), // expected slot 1 via room name reservation
            };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, "FOO: (Alice) vs (Bob)", max_slots);

            Assert.That(result[20], Is.EqualTo(0), "Alice reserved at slot 0");
            Assert.That(result[40], Is.EqualTo(1), "Bob reserved at slot 1");
            Assert.That(result[10], Is.EqualTo(2), "Carol fills the first free slot");
            Assert.That(result[30], Is.EqualTo(3), "Dave fills the next free slot");
        }

        [Test]
        public void ExcludesNonParticipatingUsersFromSlotAssignment()
        {
            // Regression test reflecting an actual captured log: a room member in Idle state
            // (present in the room but not readied up for the round) was being assigned an
            // early slot that never filled, so the grid rendered N-1 tiles for a slider value
            // of N. Only users whose state indicates participation in the current round
            // (WaitingForLoad / Loaded / ReadyForGameplay / Playing) should receive a slot.
            var users = new[]
            {
                user(921, "ilw8_dev1", MultiplayerUserState.Idle),
                user(1062, "ilw8_dev4", MultiplayerUserState.Playing),
                user(922, "ilw8_dev2", MultiplayerUserState.Playing),
                user(923, "ilw8_dev3", MultiplayerUserState.Playing),
                user(1630, "ilw8_dev9", MultiplayerUserState.Spectating),
            };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, "TRTO2026 BR1: Lobby 1", max_slots);

            Assert.That(result.ContainsKey(921), Is.False, "idle user should not receive a slot");
            Assert.That(result.ContainsKey(1630), Is.False, "spectator should not receive a slot");
            Assert.That(result[1062], Is.EqualTo(0));
            Assert.That(result[922], Is.EqualTo(1));
            Assert.That(result[923], Is.EqualTo(2));
        }

        [TestCase(MultiplayerUserState.WaitingForLoad)]
        [TestCase(MultiplayerUserState.Loaded)]
        [TestCase(MultiplayerUserState.ReadyForGameplay)]
        [TestCase(MultiplayerUserState.Playing)]
        public void IncludesUsersInAnyActiveGameplayState(MultiplayerUserState state)
        {
            // Users don't all reach Playing simultaneously — when the first user's state
            // transitions to Playing (which is when the snapshot is taken), others who are
            // still loading are in WaitingForLoad / Loaded / ReadyForGameplay. All four
            // states represent round participation and must receive slots.
            var users = new[] { user(1, "alice", state) };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, null, max_slots);

            Assert.That(result[1], Is.EqualTo(0));
        }

        [TestCase(MultiplayerUserState.Idle)]
        [TestCase(MultiplayerUserState.Ready)]
        [TestCase(MultiplayerUserState.FinishedPlay)]
        [TestCase(MultiplayerUserState.Results)]
        [TestCase(MultiplayerUserState.Spectating)]
        public void ExcludesUsersNotParticipatingInCurrentRound(MultiplayerUserState state)
        {
            var users = new[] { user(1, "alice", state) };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, null, max_slots);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void NonParticipantDoesNotClaimTeamNameReservation()
        {
            // Even if a non-participating user's username happens to match a team name, the
            // team-name reservation must not silently land on them — the matched participating
            // player should still take the reserved slot.
            var users = new[]
            {
                user(999, "Alice", MultiplayerUserState.Spectating),
                user(1, "Alice", MultiplayerUserState.Playing), // real player with the same username
                user(2, "Bob", MultiplayerUserState.Playing),
            };

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, "FOO: (Alice) vs (Bob)", max_slots);

            Assert.That(result.ContainsKey(999), Is.False);
            Assert.That(result[1], Is.EqualTo(0));
            Assert.That(result[2], Is.EqualTo(1));
        }

        [Test]
        public void TruncatesToMaxSlots()
        {
            var users = new MultiplayerRoomUser[10];
            for (int i = 0; i < users.Length; i++)
                users[i] = user(i + 1, $"user{i + 1}");

            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(users, null, max_slots);

            Assert.That(result, Has.Count.EqualTo(max_slots));
            for (int i = 0; i < max_slots; i++)
                Assert.That(result[i + 1], Is.EqualTo(i));
        }

        [Test]
        public void HandlesEmptyRoom()
        {
            var result = TournamentGameplayDisplay.BuildSnapshottedSlots(System.Array.Empty<MultiplayerRoomUser>(), null, max_slots);

            Assert.That(result, Is.Empty);
        }

        // Default to Playing so basic fixtures represent a user actively participating in the
        // round; individual tests override the state when they need a non-participating user.
        private static MultiplayerRoomUser user(int id, string username, MultiplayerUserState state = MultiplayerUserState.Playing)
            => new MultiplayerRoomUser(id)
            {
                User = new APIUser { Id = id, Username = username },
                State = state,
            };
    }
}
