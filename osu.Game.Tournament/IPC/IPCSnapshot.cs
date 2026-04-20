// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        TourneyState State,
        long Team1Score,
        long Team2Score,
        ImmutableArray<IPCUserSnapshot> Users)
    {
        public static IPCSnapshot EmptyDisconnected { get; } = new IPCSnapshot(
            Connected: false,
            RoomId: null,
            BeatmapId: null,
            State: TourneyState.Idle,
            Team1Score: 0,
            Team2Score: 0,
            Users: ImmutableArray<IPCUserSnapshot>.Empty);

        /// <summary>
        /// Serializes a snapshot to the JSON schema documented in the design spec.
        /// </summary>
        public static string SerializeToJson(IPCSnapshot snap)
        {
            var users = new JArray();
            foreach (var u in snap.Users)
            {
                var hits = new JObject();
                foreach (var (key, count) in u.Hits)
                    hits[key] = count;

                users.Add(new JObject
                {
                    ["userId"] = u.UserId,
                    ["teamId"] = u.TeamId,
                    ["score"] = u.Score,
                    ["combo"] = u.Combo,
                    ["accuracy"] = u.Accuracy,
                    ["hits"] = hits,
                    ["gameplayTimeMs"] = u.GameplayTimeMs,
                });
            }

            var root = new JObject
            {
                ["connected"] = snap.Connected,
                ["roomId"] = snap.RoomId.HasValue ? new JValue(snap.RoomId.Value) : JValue.CreateNull(),
                ["beatmapId"] = snap.BeatmapId.HasValue ? new JValue(snap.BeatmapId.Value) : JValue.CreateNull(),
                ["state"] = stateToJson(snap.State),
                ["scores"] = new JObject
                {
                    ["team1"] = snap.Team1Score,
                    ["team2"] = snap.Team2Score,
                },
                ["users"] = users,
            };

            return root.ToString(Formatting.None);
        }

        // camelCase the TourneyState enum name: Idle → "idle", WaitingForClients → "waitingForClients".
        // Consumers read this to decide whether to show gameplay HUD, ranking panel, etc.
        private static string stateToJson(TourneyState state)
        {
            string name = state.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Given a live snapshot plus the writer's persisted state (last-connected snapshot
        /// and previous-tick connection flag), returns the snapshot to actually serialize.
        /// Implements the disconnect-preservation rule from the design spec:
        /// on disconnect, reuse the last connected snapshot with <c>Connected = false</c>;
        /// on reconnect, drop the old session's last-connected snapshot and take the new one.
        /// </summary>
        public static IPCSnapshot ComputeOutput(
            IPCSnapshot live,
            ref IPCSnapshot? lastConnectedSnapshot,
            ref bool wasConnected)
        {
            if (live.Connected)
            {
                // New connection (was false, now true): drop any previous session's memory.
                if (!wasConnected)
                    lastConnectedSnapshot = null;

                lastConnectedSnapshot = live;
                wasConnected = true;
                return live;
            }

            wasConnected = false;

            if (lastConnectedSnapshot is { } remembered)
                return remembered with { Connected = false };

            return EmptyDisconnected;
        }
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
