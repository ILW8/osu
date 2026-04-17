// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Immutable snapshot of the multiplayer room state at a single point in time.
    /// Feeds JSON serialization in <see cref="MultiplayerIPCWriter"/>; the writer's dirty-check
    /// compares serialized strings (not record equality), since the compiler-generated <c>Equals</c>
    /// is reference-based for the <c>ImmutableArray</c> / <c>ImmutableDictionary</c> collection fields.
    /// </summary>
    internal readonly record struct IPCSnapshot(
        bool Connected,
        long? RoomId,
        int? BeatmapId,
        long Team1Score,
        long Team2Score,
        ImmutableArray<IPCUserSnapshot> Users)
    {
        public static IPCSnapshot EmptyDisconnected { get; } = new IPCSnapshot(
            Connected: false,
            RoomId: null,
            BeatmapId: null,
            Team1Score: 0,
            Team2Score: 0,
            Users: ImmutableArray<IPCUserSnapshot>.Empty);
    }

    /// <summary>
    /// Per-user gameplay data included in an <see cref="IPCSnapshot"/>.
    /// </summary>
    /// <param name="TeamId">1-indexed team number (internal <c>TeamVersusUserState.TeamID</c> + 1).</param>
    /// <param name="Hits">Lowercase <c>HitResult</c> enum name → count. Keys vary by ruleset.</param>
    internal readonly record struct IPCUserSnapshot(
        int UserId,
        int TeamId,
        long Score,
        int Combo,
        double Accuracy,
        ImmutableDictionary<string, int> Hits,
        double GameplayTimeMs);
}
