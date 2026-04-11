// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// An <see cref="MatchIPCInfo"/> implementation that sources data from a multiplayer room
    /// via <see cref="MultiplayerClient"/> and <see cref="SpectatorClient"/>,
    /// replacing the file-based IPC used with the stable client.
    /// </summary>
    public partial class MultiplayerMatchIPCInfo : MatchIPCInfo
    {
        /// <summary>
        /// Whether this client is currently connected to a multiplayer room.
        /// </summary>
        public IBindable<bool> IsConnected => isConnected;

        private readonly Bindable<bool> isConnected = new Bindable<bool>();

        /// <summary>
        /// The currently connected room ID, or null if not connected.
        /// </summary>
        public IBindable<long?> ConnectedRoomId => connectedRoomId;

        private readonly Bindable<long?> connectedRoomId = new Bindable<long?>();

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Resolved]
        private SpectatorClient spectatorClient { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private IRulesetStore rulesetStore { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        /// <summary>
        /// Tracks the latest total score per user from spectator frame headers.
        /// </summary>
        private readonly Dictionary<int, long> userScores = new Dictionary<int, long>();

        /// <summary>
        /// The set of user IDs we are currently watching via the spectator client.
        /// </summary>
        private readonly HashSet<int> watchedUsers = new HashSet<int>();

        private int lastBeatmapId;

        [BackgroundDependencyLoader]
        private void load()
        {
        }

        /// <summary>
        /// Connects to a multiplayer room as a spectator.
        /// </summary>
        /// <param name="roomId">The room ID to connect to.</param>
        public async Task Connect(long roomId)
        {
            Logger.Log($"[MultiplayerMatchIPCInfo] Connecting to room {roomId}", LoggingTarget.Network);

            if (isConnected.Value)
                await Disconnect().ConfigureAwait(false);

            try
            {
                // Fetch room details from the API (can run on any thread).
                var getRoomRequest = new GetRoomRequest(roomId);
                await api.PerformAsync(getRoomRequest).ConfigureAwait(false);

                var apiRoom = getRoomRequest.Response;

                if (apiRoom == null)
                {
                    Logger.Log($"[MultiplayerMatchIPCInfo] Failed to fetch room {roomId}", LoggingTarget.Network, LogLevel.Error);
                    return;
                }

                apiRoom.RoomID = roomId;

                // JoinRoom and ToggleSpectate access MultiplayerClient.Room which asserts
                // the update thread. Schedule each call separately to ensure both start
                // on the update thread (JoinRoom uses ConfigureAwait(false) internally,
                // so the continuation after the first await would be on a thread pool thread).
                Logger.Log($"[MultiplayerMatchIPCInfo] Joining room {roomId}...", LoggingTarget.Network);
                await scheduleOnUpdateThread(() => multiplayerClient.JoinRoom(apiRoom)).ConfigureAwait(false);
                Logger.Log($"[MultiplayerMatchIPCInfo] Joined room {roomId}, toggling spectate...", LoggingTarget.Network);

                // Switch to spectator mode. Non-fatal if the server rejects it.
                try
                {
                    await scheduleOnUpdateThread(() => multiplayerClient.ToggleSpectate()).ConfigureAwait(false);
                    Logger.Log($"[MultiplayerMatchIPCInfo] Now spectating room {roomId}", LoggingTarget.Network);
                }
                catch (Exception toggleEx)
                {
                    Logger.Log($"[MultiplayerMatchIPCInfo] Failed to toggle spectate (continuing in idle state): {toggleEx.GetType().Name}: {toggleEx.Message}",
                        LoggingTarget.Network, LogLevel.Important);
                }

                // Subscribe to events (safe from any thread since these are just delegate additions).
                multiplayerClient.RoomUpdated += onRoomUpdated;
                multiplayerClient.LoadRequested += onLoadRequested;
                multiplayerClient.GameplayStarted += onGameplayStarted;
                multiplayerClient.ResultsReady += onResultsReady;
                multiplayerClient.UserJoined += onUserJoined;
                multiplayerClient.UserLeft += onUserLeft;
                multiplayerClient.UserKicked += onUserKicked;
                multiplayerClient.GameplayAborted += onGameplayAborted;

                spectatorClient.OnNewFrames += onNewFrames;

                // Start watching all current users for spectator data.
                Schedule(() =>
                {
                    if (multiplayerClient.Room != null)
                    {
                        foreach (var user in multiplayerClient.Room.Users)
                        {
                            if (user.State != MultiplayerUserState.Spectating)
                                startWatchingUser(user.UserID);
                        }

                        updateBeatmapFromRoom();
                        updateModsFromRoom();
                        updateChatChannelFromRoom();
                    }

                    connectedRoomId.Value = roomId;
                    isConnected.Value = true;

                    Logger.Log($"[MultiplayerMatchIPCInfo] Connected to room {roomId}", LoggingTarget.Network);
                });
            }
            catch (Exception e)
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] Failed to connect to room {roomId}: {e.GetType().Name}: {e.Message}", LoggingTarget.Network, LogLevel.Error);
                await Disconnect().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Schedules an async operation to start on the update thread and returns a task
        /// that completes when the operation finishes.
        /// </summary>
        private Task scheduleOnUpdateThread(Func<Task> action)
        {
            var tcs = new TaskCompletionSource<bool>();

            Schedule(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// Disconnects from the current multiplayer room.
        /// </summary>
        public async Task Disconnect()
        {
            spectatorClient.OnNewFrames -= onNewFrames;

            multiplayerClient.RoomUpdated -= onRoomUpdated;
            multiplayerClient.LoadRequested -= onLoadRequested;
            multiplayerClient.GameplayStarted -= onGameplayStarted;
            multiplayerClient.ResultsReady -= onResultsReady;
            multiplayerClient.UserJoined -= onUserJoined;
            multiplayerClient.UserLeft -= onUserLeft;
            multiplayerClient.UserKicked -= onUserKicked;
            multiplayerClient.GameplayAborted -= onGameplayAborted;

            try
            {
                if (multiplayerClient.Room != null)
                    await multiplayerClient.LeaveRoom().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] Error leaving room: {e.GetType().Name}: {e.Message}", LoggingTarget.Network, LogLevel.Error);
            }

            Schedule(() =>
            {
                // Stop watching all users on the update thread to avoid racing with onNewFrames.
                foreach (int userId in watchedUsers.ToArray())
                    stopWatchingUser(userId);

                isConnected.Value = false;
                connectedRoomId.Value = null;
                lastBeatmapId = 0;
                userScores.Clear();

                // Reset bindables to defaults.
                Beatmap.Value = null;
                Mods.Value = LegacyMods.None;
                State.Value = TourneyState.Idle;
                ChatChannel.Value = string.Empty;
                Score1.Value = 0;
                Score2.Value = 0;

                Logger.Log("[MultiplayerMatchIPCInfo] Disconnected from room", LoggingTarget.Network);
            });
        }

        private void startWatchingUser(int userId)
        {
            if (!watchedUsers.Add(userId))
                return;

            spectatorClient.WatchUser(userId);
            userScores[userId] = 0;
        }

        private void stopWatchingUser(int userId)
        {
            if (!watchedUsers.Remove(userId))
                return;

            spectatorClient.StopWatchingUser(userId);
            userScores.Remove(userId);
        }

        #region Spectator event handlers

        private void onNewFrames(int userId, FrameDataBundle bundle)
        {
            if (!watchedUsers.Contains(userId))
                return;

            Schedule(() =>
            {
                userScores[userId] = bundle.Header.TotalScore;
                updateTeamScores();
            });
        }

        #endregion

        #region Multiplayer event handlers

        private void onRoomUpdated()
        {
            Schedule(() =>
            {
                updateBeatmapFromRoom();
                updateModsFromRoom();
                updateChatChannelFromRoom();
            });
        }

        private void onLoadRequested()
        {
            Schedule(() =>
            {
                State.Value = TourneyState.WaitingForClients;

                // Reset scores for the new round.
                foreach (int userId in userScores.Keys.ToArray())
                    userScores[userId] = 0;

                Score1.Value = 0;
                Score2.Value = 0;

                // Start watching users that are about to play.
                if (multiplayerClient.Room != null)
                {
                    foreach (var user in multiplayerClient.Room.Users)
                    {
                        if (user.State != MultiplayerUserState.Spectating)
                            startWatchingUser(user.UserID);
                    }
                }
            });
        }

        private void onGameplayStarted()
        {
            Schedule(() => State.Value = TourneyState.Playing);
        }

        private void onResultsReady()
        {
            Schedule(() =>
            {
                // Ensure final scores are updated before transitioning to ranking.
                updateTeamScores();
                State.Value = TourneyState.Ranking;
            });
        }

        private void onGameplayAborted(GameplayAbortReason reason)
        {
            Schedule(() => State.Value = TourneyState.Idle);
        }

        private void onUserJoined(MultiplayerRoomUser user)
        {
            Schedule(() =>
            {
                if (user.State != MultiplayerUserState.Spectating)
                    startWatchingUser(user.UserID);
            });
        }

        private void onUserLeft(MultiplayerRoomUser user)
        {
            Schedule(() => stopWatchingUser(user.UserID));
        }

        private void onUserKicked(MultiplayerRoomUser user)
        {
            Schedule(() => stopWatchingUser(user.UserID));
        }

        #endregion

        #region Data mapping

        private void updateBeatmapFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            MultiplayerPlaylistItem currentItem;

            try
            {
                currentItem = multiplayerClient.Room.CurrentPlaylistItem;
            }
            catch
            {
                return;
            }

            int beatmapId = currentItem.BeatmapID;

            if (beatmapId == lastBeatmapId)
                return;

            lastBeatmapId = beatmapId;

            // Check if the beatmap is in the current round's map pool first.
            var existing = ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == beatmapId);

            if (existing != null)
            {
                Beatmap.Value = existing.Beatmap;
            }
            else
            {
                // Fall back to API lookup.
                Task.Run(async () =>
                {
                    var apiBeatmap = await beatmapLookupCache.GetBeatmapAsync(beatmapId).ConfigureAwait(false);

                    Schedule(() =>
                    {
                        if (lastBeatmapId == beatmapId && apiBeatmap != null)
                            Beatmap.Value = new TournamentBeatmap(apiBeatmap);
                    });
                });
            }
        }

        private void updateModsFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            MultiplayerPlaylistItem currentItem;

            try
            {
                currentItem = multiplayerClient.Room.CurrentPlaylistItem;
            }
            catch
            {
                return;
            }

            var rulesetInfo = rulesetStore.GetRuleset(currentItem.RulesetID);

            if (rulesetInfo == null)
                return;

            var ruleset = rulesetInfo.CreateInstance();
            var mods = currentItem.RequiredMods.Select(m => m.ToMod(ruleset)).ToArray();
            Mods.Value = ruleset.ConvertToLegacyMods(mods);
        }

        private void updateChatChannelFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            ChatChannel.Value = multiplayerClient.Room.ChannelID.ToString();
        }

        private void updateTeamScores()
        {
            if (multiplayerClient.Room == null)
                return;

            long team0Score = 0;
            long team1Score = 0;

            foreach (var user in multiplayerClient.Room.Users)
            {
                if (user.MatchState is not TeamVersusUserState teamState)
                    continue;

                if (!userScores.TryGetValue(user.UserID, out long score))
                    continue;

                if (teamState.TeamID == 0)
                    team0Score += score;
                else
                    team1Score += score;
            }

            Score1.Value = team0Score;
            Score2.Value = team1Score;
        }

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (spectatorClient.IsNotNull())
            {
                spectatorClient.OnNewFrames -= onNewFrames;

                foreach (int userId in watchedUsers)
                    spectatorClient.StopWatchingUser(userId);
            }

            if (multiplayerClient.IsNotNull())
            {
                multiplayerClient.RoomUpdated -= onRoomUpdated;
                multiplayerClient.LoadRequested -= onLoadRequested;
                multiplayerClient.GameplayStarted -= onGameplayStarted;
                multiplayerClient.ResultsReady -= onResultsReady;
                multiplayerClient.UserJoined -= onUserJoined;
                multiplayerClient.UserLeft -= onUserLeft;
                multiplayerClient.UserKicked -= onUserKicked;
                multiplayerClient.GameplayAborted -= onGameplayAborted;
            }
        }
    }
}
