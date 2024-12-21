// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osuTK.Input;

namespace osu.Game.IPC
{
    public interface ITournamentWsControl
    {
        /// <summary>
        /// Should trigger a save of bracket.json
        /// </summary>
        public event Action? OnSaveRequested;

        /// <summary>
        /// callback taking two params: score change for team red, score change for team blue
        /// </summary>
        public event Action<int, int>? OnTeamScoreUpdateRequested;

        /// <summary>
        /// 1st param: team name (red/blue)
        /// 2nd param: is pick (false: 0, true: 1)
        /// </summary>
        public event Action<string, int> OnPickBanActionUpdate;

        public event Action<Key>? OnSceneChangeRequested;

        public event Action OnWarmupToggleRequested;

        public void BroadcastMappoolChange(Dictionary<string, int> poolSize);
    }
}
