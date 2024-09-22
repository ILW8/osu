// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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

        public event Action<Key>? OnSceneChangeRequested;
    }
}
