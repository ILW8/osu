// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Writes live multiplayer room state to <c>ipc.json</c> under the tournament
    /// storage so external overlays and scoreboards can consume it by polling.
    /// Instantiated only when multiplayer spectating is active.
    /// </summary>
    internal partial class MultiplayerIPCWriter : Component
    {
        public const string IPC_DIRECTORY = "ipc";
        public const string IPC_FILENAME = "ipc.json";
        private const string ipc_tmp_filename = "ipc.json.tmp";

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private MultiplayerMatchIPCInfo ipcInfo { get; set; } = null!;

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private Storage ipcStorage = null!;
        private ScheduledDelegate? tickDelegate;

        // Writer-owned state driving the disconnect-preservation rule.
        private IPCSnapshot? lastConnectedSnapshot;
        private bool wasConnected;

        // Last successfully-written JSON payload. The dirty-check compares the
        // serialized bytes rather than IPCSnapshot.Equals: ImmutableArray / ImmutableDictionary
        // use reference equality in the auto-generated record equality comparer, so two
        // snapshots with identical data but distinct collection backings (which is exactly
        // what each tick produces) would otherwise compare unequal and re-write every time.
        // Comparing the serialized string is also the most honest dirty-check: "dirty" =
        // "produces different bytes on disk", which is the invariant we actually care about.
        //
        // Read and written only on the update thread.
        private string? lastWrittenJson;

        // Guard against overlapping background writes: serialization + file I/O run on the
        // thread pool, and a slow disk (AV scan, network drive) could otherwise let two ticks
        // race to replace ipc.json out of order. Mutated only on the update thread.
        private bool writeInFlight;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);

            // Initial write runs synchronously during BDL (already off the update thread here)
            // so consumers polling at startup always see a valid ipc.json.
            string initialJson = IPCSnapshot.SerializeToJson(IPCSnapshot.EmptyDisconnected);
            if (writeAtomically(initialJson))
                lastWrittenJson = initialJson;

            ladder.IPCWriteIntervalMilliseconds.BindValueChanged(
                e => rescheduleTicks(e.NewValue),
                runOnceImmediately: true);
        }

        private void rescheduleTicks(int intervalMs)
        {
            tickDelegate?.Cancel();
            tickDelegate = Scheduler.AddDelayed(tick, intervalMs, true);
        }

        private void tick()
        {
            // buildLiveSnapshot reads update-thread-owned state (bindables, Room.Users,
            // UserStates) so it must run here. Serialization and file I/O are dispatched
            // to the thread pool below so a slow disk can't stall the frame loop.
            if (writeInFlight)
                return;

            var live = buildLiveSnapshot();
            var output = IPCSnapshot.ComputeOutput(live, ref lastConnectedSnapshot, ref wasConnected);
            string? expected = lastWrittenJson;

            writeInFlight = true;

            Task.Run(() =>
            {
                string json = IPCSnapshot.SerializeToJson(output);

                if (json == expected)
                {
                    Schedule(() => writeInFlight = false);
                    return;
                }

                bool success = writeAtomically(json);

                Schedule(() =>
                {
                    // Only advance lastWrittenJson on a successful write. Otherwise a transient
                    // I/O or permission failure would make the next tick short-circuit on the
                    // same payload and leave a stale ipc.json until the tracked state changes.
                    if (success)
                        lastWrittenJson = json;

                    writeInFlight = false;
                });
            });
        }

        /// <summary>
        /// Project live <see cref="MultiplayerMatchIPCInfo"/> + <see cref="MultiplayerClient.Room"/>
        /// state into an <see cref="IPCSnapshot"/>. Must run on the update thread.
        /// </summary>
        private IPCSnapshot buildLiveSnapshot()
        {
            bool connected = ipcInfo.IsConnected.Value;
            long? roomId = ipcInfo.ConnectedRoomId.Value;
            int? beatmapId = ipcInfo.Beatmap.Value?.OnlineID;
            TourneyState tourneyState = ipcInfo.State.Value;
            long score1 = ipcInfo.Score1.Value;
            long score2 = ipcInfo.Score2.Value;

            ImmutableArray<IPCUserSnapshot> users = connected && multiplayerClient.Room is { } room
                ? BuildUserSnapshots(room.Users, ipcInfo.UserStates)
                : ImmutableArray<IPCUserSnapshot>.Empty;

            return new IPCSnapshot(
                Connected: connected,
                RoomId: roomId,
                BeatmapId: beatmapId,
                State: tourneyState,
                Team1Score: score1,
                Team2Score: score2,
                Users: users);
        }

        /// <summary>
        /// Projects room users + their gameplay states into the serialized <see cref="IPCUserSnapshot"/>
        /// shape. Users are included regardless of room match type: users with a
        /// <see cref="TeamVersusUserState"/> get a 1-indexed <c>teamId</c> (internal <c>TeamID + 1</c>);
        /// users without team state (head-to-head, battle-royale) get <c>teamId = 0</c> as a
        /// "no team affiliation" sentinel so downstream consumers can distinguish them.
        /// Users missing a gameplay state entry are skipped (no frames received yet).
        /// Pure function of its arguments — extracted for unit testing.
        /// </summary>
        internal static ImmutableArray<IPCUserSnapshot> BuildUserSnapshots(
            IEnumerable<MultiplayerRoomUser> roomUsers,
            IReadOnlyDictionary<int, UserGameplayState> userStates)
        {
            var users = ImmutableArray.CreateBuilder<IPCUserSnapshot>();

            foreach (var roomUser in roomUsers)
            {
                if (!userStates.TryGetValue(roomUser.UserID, out var state))
                    continue;

                // TeamVs rooms carry team membership via TeamVersusUserState; other room types
                // (head-to-head, battle-royale) leave MatchState null. Emit 0 as a "no team"
                // sentinel so downstream consumers can distinguish them from the 1-indexed
                // TeamVs values (1 / 2).
                int teamId = roomUser.MatchState is TeamVersusUserState teamState
                    ? teamState.TeamID + 1
                    : 0;

                var hitsBuilder = ImmutableDictionary.CreateBuilder<string, int>();
                foreach (var (result, count) in state.Hits)
                    hitsBuilder[result.ToString().ToLowerInvariant()] = count;

                users.Add(new IPCUserSnapshot(
                    UserId: roomUser.UserID,
                    TeamId: teamId,
                    State: roomUser.State,
                    Role: roomUser.Role,
                    Score: state.Score,
                    Combo: state.Combo,
                    Accuracy: state.Accuracy,
                    Hits: hitsBuilder.ToImmutable(),
                    GameplayTimeMs: state.GameplayTimeMs));
            }

            return users.ToImmutable();
        }

        /// <summary>
        /// Writes <paramref name="json"/> via write-to-temp + atomic rename.
        /// Returns <c>true</c> if the file was replaced; <c>false</c> on a caught I/O or
        /// permission failure so the caller can keep retrying on subsequent ticks.
        /// </summary>
        private bool writeAtomically(string json)
        {
            string tmpFullPath = ipcStorage.GetFullPath(ipc_tmp_filename);
            string finalFullPath = ipcStorage.GetFullPath(IPC_FILENAME);

            try
            {
                File.WriteAllText(tmpFullPath, json);
                File.Move(tmpFullPath, finalFullPath, overwrite: true);
                return true;
            }
            catch (IOException e)
            {
                Logger.Log($"[MultiplayerIPCWriter] Failed to write {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                // Per-tick writes can hit permission errors repeatedly (e.g. antivirus quarantine,
                // read-only directory) — log at Important level so the operator notices, but don't
                // let it tear down the scheduler.
                Logger.Log($"[MultiplayerIPCWriter] Permission denied writing {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
                return false;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            tickDelegate?.Cancel();
            // Explicitly unbind from the long-lived LadderInfo bindable so the callback's
            // closure over `this` doesn't keep the writer alive past disposal.
            ladder?.IPCWriteIntervalMilliseconds.UnbindEvents();
        }
    }
}
