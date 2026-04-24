// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    /// <summary>
    /// Unit tests for <see cref="MultiplayerIPCWriter.BuildUserSnapshots"/>, the pure projection
    /// from multiplayer room users + per-user gameplay state to the IPC user array.
    /// </summary>
    [TestFixture]
    public class MultiplayerIPCWriterBuildUserSnapshotsTest
    {
        [Test]
        public void IncludesUsersWithoutMatchState()
        {
            // Non-TeamVs rooms (head-to-head, battle-royale) leave MatchState null on users.
            // The IPC output must still carry their gameplay data so external overlays can render.
            var roomUsers = new[]
            {
                new MultiplayerRoomUser(userId: 42) { State = MultiplayerUserState.Playing },
            };

            var states = new Dictionary<int, UserGameplayState>
            {
                [42] = new UserGameplayState(
                    Score: 100,
                    Combo: 5,
                    Accuracy: 0.9,
                    Hits: new Dictionary<HitResult, int> { [HitResult.Great] = 10 },
                    GameplayTimeMs: 1000),
            };

            var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, states);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].UserId, Is.EqualTo(42));
            Assert.That(result[0].TeamId, Is.EqualTo(0), "users without a team state should surface as teamId=0");
            Assert.That(result[0].State, Is.EqualTo(MultiplayerUserState.Playing));
            Assert.That(result[0].Role, Is.EqualTo(MultiplayerRoomUserRole.Player));
            Assert.That(result[0].Score, Is.EqualTo(100));
            Assert.That(result[0].Combo, Is.EqualTo(5));
            Assert.That(result[0].Hits["great"], Is.EqualTo(10));
        }

        [Test]
        public void ProjectsStateAndRoleFromRoomUser()
        {
            // Headline motivation for this field pair: a referee and a player sharing users[]
            // must be distinguishable downstream. Role flows through from MultiplayerRoomUser.Role,
            // State from MultiplayerRoomUser.State, so consumers can split referees out and tell
            // idle / ready / playing participants apart.
            var roomUsers = new[]
            {
                new MultiplayerRoomUser(userId: 1)
                {
                    State = MultiplayerUserState.Playing,
                    Role = MultiplayerRoomUserRole.Player,
                },
                new MultiplayerRoomUser(userId: 2)
                {
                    State = MultiplayerUserState.Idle,
                    Role = MultiplayerRoomUserRole.Referee,
                },
                new MultiplayerRoomUser(userId: 3)
                {
                    State = MultiplayerUserState.FinishedPlay,
                    Role = MultiplayerRoomUserRole.Player,
                },
            };

            var states = new Dictionary<int, UserGameplayState>
            {
                [1] = UserGameplayState.Empty,
                [2] = UserGameplayState.Empty,
                [3] = UserGameplayState.Empty,
            };

            var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, states);

            Assert.That(
                result.Select(u => (u.UserId, u.State, u.Role)),
                Is.EquivalentTo(new[]
                {
                    (1, MultiplayerUserState.Playing, MultiplayerRoomUserRole.Player),
                    (2, MultiplayerUserState.Idle, MultiplayerRoomUserRole.Referee),
                    (3, MultiplayerUserState.FinishedPlay, MultiplayerRoomUserRole.Player),
                }));
        }

        [Test]
        public void Projects1IndexedTeamIdsForTeamVersusUsers()
        {
            var roomUsers = new[]
            {
                new MultiplayerRoomUser(userId: 1) { MatchState = new TeamVersusUserState { TeamID = 0 } },
                new MultiplayerRoomUser(userId: 2) { MatchState = new TeamVersusUserState { TeamID = 1 } },
            };

            var states = new Dictionary<int, UserGameplayState>
            {
                [1] = UserGameplayState.Empty,
                [2] = UserGameplayState.Empty,
            };

            var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, states);

            Assert.That(
                result.Select(u => (u.UserId, u.TeamId)),
                Is.EquivalentTo(new[] { (1, 1), (2, 2) }));
        }

        [Test]
        public void SkipsUsersWithoutGameplayState()
        {
            // A user in the room but with no frames received yet has no UserGameplayState entry.
            // Keep the existing behavior of omitting them from the snapshot.
            var roomUsers = new[]
            {
                new MultiplayerRoomUser(userId: 42),
            };

            var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, new Dictionary<int, UserGameplayState>());

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void MixedRoomPreservesTeamIdsAndNoTeamSentinel()
        {
            // If a room somehow holds both TeamVs and non-TeamVs users, each is projected per its own state.
            var roomUsers = new[]
            {
                new MultiplayerRoomUser(userId: 1) { MatchState = new TeamVersusUserState { TeamID = 1 } },
                new MultiplayerRoomUser(userId: 2),
            };

            var states = new Dictionary<int, UserGameplayState>
            {
                [1] = UserGameplayState.Empty,
                [2] = UserGameplayState.Empty,
            };

            var result = MultiplayerIPCWriter.BuildUserSnapshots(roomUsers, states);

            Assert.That(
                result.Select(u => (u.UserId, u.TeamId)),
                Is.EquivalentTo(new[] { (1, 2), (2, 0) }));
        }
    }
}
