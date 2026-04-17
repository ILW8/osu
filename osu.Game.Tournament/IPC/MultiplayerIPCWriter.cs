// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using System.IO;
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
        private string? lastWrittenJson;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);

            string initialJson = IPCSnapshot.SerializeToJson(IPCSnapshot.EmptyDisconnected);
            writeAtomically(initialJson);
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
            var live = buildLiveSnapshot();
            var output = IPCSnapshot.ComputeOutput(live, ref lastConnectedSnapshot, ref wasConnected);
            string json = IPCSnapshot.SerializeToJson(output);

            if (json == lastWrittenJson)
                return;

            writeAtomically(json);
            lastWrittenJson = json;
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
            long score1 = ipcInfo.Score1.Value;
            long score2 = ipcInfo.Score2.Value;

            var users = ImmutableArray.CreateBuilder<IPCUserSnapshot>();

            if (connected && multiplayerClient.Room is { } room)
            {
                foreach (var roomUser in room.Users)
                {
                    if (roomUser.MatchState is not TeamVersusUserState teamState)
                        continue;

                    if (!ipcInfo.UserStates.TryGetValue(roomUser.UserID, out var state))
                        continue;

                    var hitsBuilder = ImmutableDictionary.CreateBuilder<string, int>();
                    foreach (var (result, count) in state.Hits)
                        hitsBuilder[result.ToString().ToLowerInvariant()] = count;

                    users.Add(new IPCUserSnapshot(
                        UserId: roomUser.UserID,
                        TeamId: teamState.TeamID + 1, // 1-indexed per schema
                        Score: state.Score,
                        Combo: state.Combo,
                        Accuracy: state.Accuracy,
                        Hits: hitsBuilder.ToImmutable(),
                        GameplayTimeMs: state.GameplayTimeMs));
                }
            }

            return new IPCSnapshot(
                Connected: connected,
                RoomId: roomId,
                BeatmapId: beatmapId,
                Team1Score: score1,
                Team2Score: score2,
                Users: users.ToImmutable());
        }

        private void writeAtomically(string json)
        {
            string tmpFullPath = ipcStorage.GetFullPath(ipc_tmp_filename);
            string finalFullPath = ipcStorage.GetFullPath(IPC_FILENAME);

            try
            {
                File.WriteAllText(tmpFullPath, json);
                File.Move(tmpFullPath, finalFullPath, overwrite: true);
            }
            catch (IOException e)
            {
                Logger.Log($"[MultiplayerIPCWriter] Failed to write {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
            catch (System.UnauthorizedAccessException e)
            {
                // Per-tick writes can hit permission errors repeatedly (e.g. antivirus quarantine,
                // read-only directory) — log at Important level so the operator notices, but don't
                // let it tear down the scheduler.
                Logger.Log($"[MultiplayerIPCWriter] Permission denied writing {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            tickDelegate?.Cancel();
        }
    }
}
