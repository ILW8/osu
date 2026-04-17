// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Per-user gameplay snapshot derived from a spectator <see cref="osu.Game.Online.Spectator.FrameDataBundle"/>.
    /// </summary>
    internal readonly record struct UserGameplayState(
        long Score,
        int Combo,
        double Accuracy,
        IReadOnlyDictionary<HitResult, int> Hits,
        double GameplayTimeMs)
    {
        public static UserGameplayState Empty { get; } = new UserGameplayState(
            Score: 0,
            Combo: 0,
            Accuracy: 0,
            Hits: new Dictionary<HitResult, int>(),
            GameplayTimeMs: 0);
    }
}
