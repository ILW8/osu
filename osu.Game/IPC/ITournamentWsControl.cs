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

        /// <summary>
        /// 1st param: mod string (e.g. "HD")
        /// 2nd param: slot index (1-indexed)
        /// </summary>
        public event Action<string, int>? OnPerformPickBanRequested;

        public event Action<Key>? OnSceneChangeRequested;

        public event Action OnWarmupToggleRequested;

        /// <summary>
        ///
        /// </summary>
        /// <param name="poolSize">Dictionary mapping slot name (e.g. "NM1") to pick status. Pick status is a dict with key "banned" and "team", both ints. null value on "team" means unpicked</param>
        public void BroadcastMappoolChange(Dictionary<string, Dictionary<string, int?>> poolSize);
    }
}
