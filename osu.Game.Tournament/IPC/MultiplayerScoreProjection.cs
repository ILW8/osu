// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            IReadOnlyDictionary<int, UserGameplayState> userStates,
            string? roomName = null)
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

            var playerUsers = roomUsers.Where(user => user.Role == MultiplayerRoomUserRole.Player).ToList();

            if (tryBuildRoomNameSlots(playerUsers, roomName) is { } roomNameSlots)
            {
                foreach (var (userId, slot) in roomNameSlots)
                {
                    if (!userStates.TryGetValue(userId, out var state))
                        continue;

                    switch (slot)
                    {
                        case 0:
                            team1Score = state.Score;
                            break;

                        case 1:
                            team2Score = state.Score;
                            break;
                    }
                }

                return new MultiplayerTeamScores(team1Score, team2Score);
            }

            int fallbackSlot = 0;

            foreach (var user in playerUsers)
            {
                if (!userStates.TryGetValue(user.UserID, out var state))
                    continue;

                switch (fallbackSlot++)
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

        private static readonly Regex room_name_teams_regex = new Regex(
            @"^[^:]*:\s*\((?<p1>.+?)\)\s+vs\s+\((?<p2>.+?)\)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static Dictionary<int, int>? tryBuildRoomNameSlots(IReadOnlyList<MultiplayerRoomUser> users, string? roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                return null;

            var match = room_name_teams_regex.Match(roomName);

            if (!match.Success)
                return null;

            var result = new Dictionary<int, int>();

            reserveSlot(match.Groups["p1"].Value.Trim(), 0);
            reserveSlot(match.Groups["p2"].Value.Trim(), 1);

            int nextSlot = 0;

            foreach (var user in users)
            {
                if (result.ContainsKey(user.UserID))
                    continue;

                while (nextSlot < 2 && result.ContainsValue(nextSlot))
                    nextSlot++;

                if (nextSlot >= 2)
                    break;

                result[user.UserID] = nextSlot++;
            }

            return result;

            void reserveSlot(string username, int slot)
            {
                var user = users.FirstOrDefault(u =>
                    string.Equals(u.User?.Username, username, System.StringComparison.OrdinalIgnoreCase));

                if (user != null)
                    result[user.UserID] = slot;
            }
        }
    }
}
