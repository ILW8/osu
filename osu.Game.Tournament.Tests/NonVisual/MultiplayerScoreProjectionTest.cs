// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class MultiplayerScoreProjectionTest
    {
        [Test]
        public void TeamVersusScoresAreSummedByTeam()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 1) { MatchState = new TeamVersusUserState { TeamID = 0 } },
                new MultiplayerRoomUser(userId: 2) { MatchState = new TeamVersusUserState { TeamID = 0 } },
                new MultiplayerRoomUser(userId: 3) { MatchState = new TeamVersusUserState { TeamID = 1 } },
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [1] = stateWithScore(100),
                [2] = stateWithScore(250),
                [3] = stateWithScore(300),
            });

            Assert.That(scores.Team1, Is.EqualTo(350));
            Assert.That(scores.Team2, Is.EqualTo(300));
        }

        [Test]
        public void HeadToHeadScoresUseFirstTwoPlayerUserStates()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 1),
                new MultiplayerRoomUser(userId: 2),
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [1] = stateWithScore(123456),
                [2] = stateWithScore(654321),
            });

            Assert.That(scores.Team1, Is.EqualTo(123456));
            Assert.That(scores.Team2, Is.EqualTo(654321));
        }

        [Test]
        public void HeadToHeadScoresRespectRoomNameUserOrder()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 3) { User = new APIUser { Id = 3, Username = "dev3" } },
                new MultiplayerRoomUser(userId: 2) { User = new APIUser { Id = 2, Username = "dev2" } },
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [2] = stateWithScore(222222),
                [3] = stateWithScore(333333),
            }, "LGA: (dev2) vs (dev3)");

            Assert.That(scores.Team1, Is.EqualTo(222222));
            Assert.That(scores.Team2, Is.EqualTo(333333));
        }

        [Test]
        public void RoomNameSlotsDoNotShiftWhenLeftUserHasNoGameplayState()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 3) { User = new APIUser { Id = 3, Username = "dev3" } },
                new MultiplayerRoomUser(userId: 2) { User = new APIUser { Id = 2, Username = "dev2" } },
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [3] = stateWithScore(333333),
            }, "LGA: (dev2) vs (dev3)");

            Assert.That(scores.Team1, Is.Zero);
            Assert.That(scores.Team2, Is.EqualTo(333333));
        }

        [Test]
        public void RefereesAreIgnoredForHeadToHeadScoreSlots()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 99) { Role = MultiplayerRoomUserRole.Referee },
                new MultiplayerRoomUser(userId: 1),
                new MultiplayerRoomUser(userId: 2),
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [99] = stateWithScore(999999),
                [1] = stateWithScore(10),
                [2] = stateWithScore(20),
            });

            Assert.That(scores.Team1, Is.EqualTo(10));
            Assert.That(scores.Team2, Is.EqualTo(20));
        }

        [Test]
        public void MissingGameplayStatesDoNotOccupyHeadToHeadScoreSlots()
        {
            var users = new[]
            {
                new MultiplayerRoomUser(userId: 1),
                new MultiplayerRoomUser(userId: 2),
                new MultiplayerRoomUser(userId: 3),
            };

            var scores = MultiplayerScoreProjection.CalculateTeamScores(users, new Dictionary<int, UserGameplayState>
            {
                [2] = stateWithScore(200),
                [3] = stateWithScore(300),
            });

            Assert.That(scores.Team1, Is.EqualTo(200));
            Assert.That(scores.Team2, Is.EqualTo(300));
        }

        private static UserGameplayState stateWithScore(long score) => new UserGameplayState(
            Score: score,
            Combo: 0,
            Accuracy: 0,
            Hits: new Dictionary<HitResult, int>(),
            GameplayTimeMs: 0);
    }
}
