// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;

namespace osu.Game.Tournament.IPC
{
    internal readonly record struct MultiplayerTeamScores(long Team1, long Team2);

    /// <summary>
    /// Projects multiplayer room users and their latest gameplay state into the two live
    /// score channels consumed by the tournament gameplay score display and IPC output.
    /// </summary>
    internal static class MultiplayerScoreProjection
    {
        public static MultiplayerTeamScores CalculateTeamScores(
            IEnumerable<MultiplayerRoomUser> roomUsers,
            IReadOnlyDictionary<int, UserGameplayState> userStates)
        {
            long team1Score = 0;
            long team2Score = 0;
            bool hasTeamVersusState = false;

            foreach (var user in roomUsers)
            {
                if (user.Role != MultiplayerRoomUserRole.Player)
                    continue;

                if (user.MatchState is not TeamVersusUserState teamState)
                    continue;

                hasTeamVersusState = true;

                if (!userStates.TryGetValue(user.UserID, out var state))
                    continue;

                switch (teamState.TeamID)
                {
                    case 0:
                        team1Score += state.Score;
                        break;

                    case 1:
                        team2Score += state.Score;
                        break;
                }
            }

            if (hasTeamVersusState)
                return new MultiplayerTeamScores(team1Score, team2Score);

            int slot = 0;

            foreach (var user in roomUsers)
            {
                if (user.Role != MultiplayerRoomUserRole.Player)
                    continue;

                if (!userStates.TryGetValue(user.UserID, out var state))
                    continue;

                switch (slot++)
                {
                    case 0:
                        team1Score = state.Score;
                        break;

                    case 1:
                        team2Score = state.Score;
                        break;

                    default:
                        return new MultiplayerTeamScores(team1Score, team2Score);
                }
            }

            return new MultiplayerTeamScores(team1Score, team2Score);
        }
    }
}
