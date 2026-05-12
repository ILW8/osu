// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using osu.Game.Online.Multiplayer;

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

                var mods = new JArray();
                foreach (var m in u.Mods)
                {
                    var settings = new JObject();
                    foreach (var (key, value) in m.Settings)
                        settings[key] = JToken.FromObject(value);

                    mods.Add(new JObject
                    {
                        ["acronym"] = m.Acronym,
                        ["settings"] = settings,
                    });
                }

                users.Add(new JObject
                {
                    ["userId"] = u.UserId,
                    ["teamId"] = u.TeamId,
                    ["state"] = enumNameToCamelCase(u.State),
                    ["role"] = enumNameToCamelCase(u.Role),
                    ["score"] = u.Score,
                    ["combo"] = u.Combo,
                    ["accuracy"] = u.Accuracy,
                    ["hits"] = hits,
                    ["gameplayTimeMs"] = u.GameplayTimeMs,
                    ["mods"] = mods,
                });
            }

            var root = new JObject
            {
                ["connected"] = snap.Connected,
                ["roomId"] = snap.RoomId.HasValue ? new JValue(snap.RoomId.Value) : JValue.CreateNull(),
                ["beatmapId"] = snap.BeatmapId.HasValue ? new JValue(snap.BeatmapId.Value) : JValue.CreateNull(),
                ["state"] = enumNameToCamelCase(snap.State),
                ["scores"] = new JObject
                {
                    ["team1"] = snap.Team1Score,
                    ["team2"] = snap.Team2Score,
                },
                ["users"] = users,
            };

            return root.ToString(Formatting.None);
        }

        // camelCase an enum name: Idle → "idle", WaitingForClients → "waitingForClients".
        // Shared across the room-level TourneyState and the per-user MultiplayerUserState /
        // MultiplayerRoomUserRole enums so the JSON wire format stays consistent.
        private static string enumNameToCamelCase<T>(T value) where T : struct, Enum
        {
            string name = value.ToString();
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
    /// <c>TeamId</c> is 1-indexed (internal <c>TeamVersusUserState.TeamID</c> + 1).
    /// <c>State</c> mirrors <see cref="MultiplayerRoomUser.State"/> so consumers can tell active
    /// round participants from idle / finished members. <c>Role</c> mirrors
    /// <see cref="MultiplayerRoomUser.Role"/> so consumers can split referees out of <c>users[]</c>.
    /// <c>Hits</c> keys are lowercase <c>HitResult</c> enum names (values vary by ruleset).
    /// </summary>
    internal readonly record struct IPCUserSnapshot(
        int UserId,
        int TeamId,
        MultiplayerUserState State,
        MultiplayerRoomUserRole Role,
        long Score,
        int Combo,
        double Accuracy,
        ImmutableDictionary<string, int> Hits,
        double GameplayTimeMs,
        ImmutableArray<IPCUserModEntry> Mods);

    /// <summary>
    /// Per-user mod entry within <see cref="IPCUserSnapshot.Mods"/>. Wire shape mirrors
    /// <see cref="osu.Game.Online.API.APIMod"/>: an acronym plus a snake_case settings dict.
    /// </summary>
    internal readonly record struct IPCUserModEntry(
        string Acronym,
        ImmutableDictionary<string, object> Settings);
}
