// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Plays the current multiplayer-room beatmap on loop while the tournament
    /// overlay is connected to a room and gameplay isn't actively rendered.
    /// </summary>
    public partial class TournamentLobbyMusic : Component
    {
        /// <summary>
        /// Pure decision: should we be playing lobby music right now?
        /// Music plays only when connected to a room with a locally-resolved beatmap
        /// and the IPC state is Idle or WaitingForClients (gameplay master clock
        /// owns audio during Playing and Ranking).
        /// </summary>
        public static bool ShouldPlay(bool isConnected, TourneyState state, bool hasResolvedBeatmap)
        {
            if (!isConnected || !hasResolvedBeatmap)
                return false;

            return state == TourneyState.Idle || state == TourneyState.WaitingForClients;
        }
    }
}
