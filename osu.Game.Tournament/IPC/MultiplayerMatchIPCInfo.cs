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
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Scoring;
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

        private string? connectedRoomPassword;

        /// <summary>
        /// A user-facing error message from the last failed connection attempt, or null if no error.
        /// </summary>
        public IBindable<string?> ConnectionError => connectionError;

        private readonly Bindable<string?> connectionError = new Bindable<string?>();

        /// <summary>
        /// A pending room invitation awaiting user approval, or null if none.
        /// </summary>
        public IBindable<PendingInvite?> PendingInvite => pendingInvite;

        private readonly Bindable<PendingInvite?> pendingInvite = new Bindable<PendingInvite?>();

        /// <summary>
        /// Whether any user in the room was already participating in a round
        /// (Playing / Loading / Ready-for-gameplay) at the moment the connection
        /// succeeded. Set before <see cref="IsConnected"/> flips to <c>true</c> so
        /// IsConnected listeners read a consistent snapshot. Reset on disconnect.
        /// </summary>
        public bool JoinedDuringGameplay { get; private set; }

        /// <summary>
        /// <c>true</c> while at least one user we're spectating is reporting
        /// <see cref="SpectatedUserState.Playing"/>. Driven directly from
        /// <see cref="SpectatorClient.WatchedUserStates"/> on this component, which is always
        /// alive — unlike a <c>Drawable</c>-bound observer it doesn't depend on any screen
        /// being visible. Consumers (e.g. <c>MapPoolScreen</c> auto-advance) get a reliable
        /// "spectator gameplay has begun" signal regardless of which screen is currently active.
        /// </summary>
        public IBindable<bool> HasActiveSpectatorPlayers => hasActiveSpectatorPlayers;

        private readonly Bindable<bool> hasActiveSpectatorPlayers = new Bindable<bool>();

        private readonly IBindableDictionary<int, SpectatorState> watchedUserStates = new BindableDictionary<int, SpectatorState>();

        private void recomputeHasActiveSpectatorPlayers()
        {
            bool anyPlaying = watchedUserStates.Values.Any(s => s.State == SpectatedUserState.Playing);

            if (hasActiveSpectatorPlayers.Value != anyPlaying)
                hasActiveSpectatorPlayers.Value = anyPlaying;
        }

        /// <summary>
        /// Accepts the current pending invite and connects to the room.
        /// </summary>
        public void AcceptPendingInvite()
        {
            var invite = pendingInvite.Value;

            if (invite == null)
                return;

            pendingInvite.Value = null;
            Connect(invite.RoomId, invite.Password).FireAndForget();
        }

        /// <summary>
        /// Dismisses the current pending invite without connecting.
        /// </summary>
        public void DismissPendingInvite()
        {
            pendingInvite.Value = null;
        }

        /// <summary>
        /// Sets a pending invite, scheduling the update to the update thread.
        /// </summary>
        public void SetPendingInvite(PendingInvite invite)
        {
            Schedule(() => pendingInvite.Value = invite);
        }

        /// <summary>
        /// Test-only helper: forces the connection state without going through <see cref="Connect"/>.
        /// Allows integration tests to drive IPC-writer behavior without a live SignalR connection.
        /// </summary>
        internal void SetConnectedForTesting(bool value, long? roomId = null, bool joinedDuringGameplay = false)
        {
            // Match Connect's ordering: snapshot fields populated before the isConnected flip
            // so listeners reading JoinedDuringGameplay in IsConnected handlers see a consistent state.
            JoinedDuringGameplay = joinedDuringGameplay;
            connectedRoomId.Value = roomId;
            isConnected.Value = value;
        }

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
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        /// <summary>
        /// Tracks the latest gameplay snapshot per user from spectator frame bundles.
        /// Exposed via <see cref="UserStates"/> for the IPC writer.
        /// </summary>
        private readonly Dictionary<int, UserGameplayState> userStates = new Dictionary<int, UserGameplayState>();

        /// <summary>
        /// Read-only view of the latest per-user gameplay state. Mutated on the update thread
        /// from spectator frame bundles; consumers should also read from the update thread.
        /// </summary>
        internal IReadOnlyDictionary<int, UserGameplayState> UserStates => userStates;

        /// <summary>
        /// The set of user IDs we are currently watching via the spectator client.
        /// </summary>
        private readonly HashSet<int> watchedUsers = new HashSet<int>();

        private int lastBeatmapId;

        /// <summary>
        /// Delay before <see cref="TourneyState.Ranking"/> auto-resets back to <see cref="TourneyState.Idle"/>.
        /// Anything that consumes State == Idle (lobby music, gameplay screen auto-advance) hangs off this.
        /// </summary>
        public const double RANKING_TO_IDLE_DELAY_MS = 20_000;

        private ScheduledDelegate? scheduledRankingReset;

        [BackgroundDependencyLoader]
        private void load()
        {
            watchedUserStates.BindTo(spectatorClient.WatchedUserStates);
            watchedUserStates.BindCollectionChanged((_, _) => recomputeHasActiveSpectatorPlayers(), true);
        }

        /// <summary>
        /// Connects to a multiplayer room as a spectator.
        /// </summary>
        /// <param name="roomId">The room ID to connect to.</param>
        /// <param name="password">The room password, or null if the room has no password.</param>
        public async Task Connect(long roomId, string? password = null)
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
                Schedule(() => connectionError.Value = null);
                await scheduleOnUpdateThread(() => multiplayerClient.JoinRoom(apiRoom, password)).ConfigureAwait(false);
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
                multiplayerClient.UserStateChanged += onUserStateChanged;
                multiplayerClient.GameplayAborted += onGameplayAborted;

                spectatorClient.OnNewFrames += onNewFrames;

                // Start watching users who are already participating in the current round.
                // Users in Idle/Ready/etc. will be picked up via UserStateChanged when they
                // transition into a participating state.
                Schedule(() =>
                {
                    bool joinedDuringGameplay = false;

                    if (multiplayerClient.Room != null)
                    {
                        foreach (var user in multiplayerClient.Room.Users)
                        {
                            if (isParticipatingInCurrentRound(user.State))
                            {
                                joinedDuringGameplay = true;
                                startWatchingUser(user.UserID);
                            }
                        }

                        updateBeatmapFromRoom();
                        updateModsFromRoom();
                        updateChatChannelFromRoom();
                    }

                    // Must precede the isConnected flip so listeners observe a consistent snapshot.
                    JoinedDuringGameplay = joinedDuringGameplay;
                    connectedRoomId.Value = roomId;
                    connectedRoomPassword = password;
                    isConnected.Value = true;

                    Logger.Log($"[MultiplayerMatchIPCInfo] Connected to room {roomId}", LoggingTarget.Network);
                });
            }
            catch (Exception e)
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] Failed to connect to room {roomId}: {e.GetType().Name}: {e.Message}", LoggingTarget.Network, LogLevel.Error);

                string errorMessage = e.ToString().Contains("InvalidPasswordException")
                    ? "Invalid password"
                    : $"Failed to connect: {e.InnerException?.Message ?? e.Message}";

                Schedule(() => connectionError.Value = errorMessage);
                await Disconnect().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disconnects from the current multiplayer room, then reconnects to the same room after a delay.
        /// </summary>
        /// <param name="delayMilliseconds">The delay between disconnecting and reconnecting.</param>
        public async Task Reconnect(int delayMilliseconds = 500)
        {
            long? roomId = connectedRoomId.Value;
            string? password = connectedRoomPassword;

            if (roomId == null)
            {
                Schedule(() => connectionError.Value = "No room to reconnect to");
                return;
            }

            await Disconnect().ConfigureAwait(false);
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            await Connect(roomId.Value, password).ConfigureAwait(false);
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
            multiplayerClient.UserStateChanged -= onUserStateChanged;
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
                cancelScheduledRankingReset();

                // Stop watching all users on the update thread to avoid racing with onNewFrames.
                foreach (int userId in watchedUsers.ToArray())
                    stopWatchingUser(userId);

                isConnected.Value = false;
                connectedRoomId.Value = null;
                connectedRoomPassword = null;
                JoinedDuringGameplay = false;
                lastBeatmapId = 0;
                userStates.Clear();

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
            userStates[userId] = UserGameplayState.Empty;
        }

        private void stopWatchingUser(int userId)
        {
            if (!watchedUsers.Remove(userId))
                return;

            spectatorClient.StopWatchingUser(userId);
            userStates.Remove(userId);
        }

        #region Spectator event handlers

        private void onNewFrames(int userId, FrameDataBundle bundle)
        {
            if (!watchedUsers.Contains(userId))
                return;

            Schedule(() =>
            {
                var header = bundle.Header;
                double gameplayTime = bundle.Frames.Count > 0 ? bundle.Frames[^1].Time : 0;

                // Preserve any previously-populated mods (sourced from RoomUpdated) — frame
                // bundles don't carry per-user mods, only score/combo/accuracy/hits.
                var previousMods = userStates.TryGetValue(userId, out var existing)
                    ? existing.Mods
                    : Array.Empty<APIMod>();

                userStates[userId] = new UserGameplayState(
                    Score: header.TotalScore,
                    Combo: header.Combo,
                    Accuracy: header.Accuracy,
                    Hits: new Dictionary<HitResult, int>(header.Statistics),
                    GameplayTimeMs: gameplayTime,
                    Mods: previousMods);

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
                updateUserModsFromRoom();
                updateChatChannelFromRoom();
            });
        }

        private void onLoadRequested()
        {
            Schedule(() =>
            {
                cancelScheduledRankingReset();
                State.Value = TourneyState.WaitingForClients;

                // Reset per-user state for the new round. Users are re-populated on next frame.
                foreach (int userId in userStates.Keys.ToArray())
                {
                    // Preserve mods across the load reset — they don't change between rounds.
                    // The next RoomUpdated tick refreshes them anyway, but this avoids a brief
                    // window where the overlay reads back empty mod lists.
                    var preservedMods = userStates[userId].Mods;
                    userStates[userId] = UserGameplayState.Empty with { Mods = preservedMods };
                }

                Score1.Value = 0;
                Score2.Value = 0;

                // Start watching users that are about to play.
                if (multiplayerClient.Room != null)
                {
                    foreach (var user in multiplayerClient.Room.Users)
                    {
                        if (isParticipatingInCurrentRound(user.State))
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

                // Auto-restore Idle so consumers gated on lobby state (lobby music, the
                // GameplayScreen Idle->MapPool auto-advance) get a deterministic signal that
                // results have been on screen long enough and the room is back in lobby.
                cancelScheduledRankingReset();
                scheduledRankingReset = Scheduler.AddDelayed(() =>
                {
                    if (State.Value == TourneyState.Ranking)
                        State.Value = TourneyState.Idle;
                }, RANKING_TO_IDLE_DELAY_MS);
            });
        }

        private void onGameplayAborted(GameplayAbortReason reason)
        {
            Schedule(() =>
            {
                cancelScheduledRankingReset();
                State.Value = TourneyState.Idle;
            });
        }

        private void cancelScheduledRankingReset()
        {
            scheduledRankingReset?.Cancel();
            scheduledRankingReset = null;
        }

        private void onUserJoined(MultiplayerRoomUser user)
        {
            Schedule(() =>
            {
                if (isParticipatingInCurrentRound(user.State))
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

        private void onUserStateChanged(MultiplayerRoomUser user, MultiplayerUserState state)
        {
            // Pick up users transitioning into a participating state (e.g. Ready → WaitingForLoad
            // when the host starts the round). startWatchingUser is idempotent for already-watched
            // users. Watches are not torn down on transition out — keeping them alive across
            // play→FinishedPlay→Results→Idle→Ready→WaitingForLoad avoids losing the final frame
            // bundle (which carries final score/accuracy) and avoids re-subscription churn each
            // round. Watches are released when the user leaves the room or on disconnect.
            if (isParticipatingInCurrentRound(state))
                Schedule(() => startWatchingUser(user.UserID));
        }

        private static bool isParticipatingInCurrentRound(MultiplayerUserState state)
            => state == MultiplayerUserState.WaitingForLoad
               || state == MultiplayerUserState.Loaded
               || state == MultiplayerUserState.ReadyForGameplay
               || state == MultiplayerUserState.Playing;

        #endregion

        #region Data mapping

        private void updateBeatmapFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            if (multiplayerClient.Room.Playlist.Count == 0)
                return;

            var currentItem = multiplayerClient.Room.CurrentPlaylistItem;

            int beatmapId = currentItem.BeatmapID;

            if (beatmapId == lastBeatmapId)
                return;

            lastBeatmapId = beatmapId;

            // Check if the beatmap is in the current round's map pool first.
            var existing = ladder.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == beatmapId);

            if (existing != null)
            {
                Beatmap.Value = existing.Beatmap;
                // Ensure the pool beatmap is downloaded locally for gameplay rendering.
                ensureBeatmapDownloadedById(beatmapId);
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

                    // Ensure the beatmap is downloaded locally for gameplay rendering.
                    if (apiBeatmap != null)
                        ensureBeatmapDownloaded(apiBeatmap);
                });
            }
        }

        /// <summary>
        /// Checks if a beatmap is locally available, and triggers a download if not.
        /// </summary>
        private void ensureBeatmapDownloadedById(int beatmapId)
        {
            var localBeatmap = beatmapManager.QueryBeatmap(b => b.OnlineID == beatmapId);

            if (localBeatmap != null)
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] Beatmap {beatmapId} is locally available", LoggingTarget.Network);
                return;
            }

            // Need API info to download — trigger a lookup if not already done.
            Task.Run(async () =>
            {
                var apiBeatmap = await beatmapLookupCache.GetBeatmapAsync(beatmapId).ConfigureAwait(false);

                if (apiBeatmap != null)
                    ensureBeatmapDownloaded(apiBeatmap);
            });
        }

        /// <summary>
        /// Downloads a beatmap set if it's not already available locally or being downloaded.
        /// </summary>
        private void ensureBeatmapDownloaded(APIBeatmap apiBeatmap)
        {
            Schedule(() =>
            {
                // Check if already available locally.
                if (beatmapManager.QueryBeatmap(b => b.OnlineID == apiBeatmap.OnlineID) != null)
                    return;

                var beatmapSet = apiBeatmap.BeatmapSet;

                if (beatmapSet == null)
                {
                    Logger.Log($"[MultiplayerMatchIPCInfo] Cannot download beatmap {apiBeatmap.OnlineID}: no beatmap set info", LoggingTarget.Network, LogLevel.Important);
                    return;
                }

                // Check if already downloading.
                if (beatmapDownloader.GetExistingDownload(beatmapSet) != null)
                    return;

                Logger.Log($"[MultiplayerMatchIPCInfo] Downloading beatmap set {beatmapSet.OnlineID} for beatmap {apiBeatmap.OnlineID}", LoggingTarget.Network);
                beatmapDownloader.Download(beatmapSet);
            });
        }

        private void updateModsFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            if (multiplayerClient.Room.Playlist.Count == 0)
                return;

            var currentItem = multiplayerClient.Room.CurrentPlaylistItem;

            var rulesetInfo = rulesetStore.GetRuleset(currentItem.RulesetID);

            if (rulesetInfo == null)
                return;

            var ruleset = rulesetInfo.CreateInstance();
            var mods = currentItem.RequiredMods.Select(m => m.ToMod(ruleset)).ToArray();
            Mods.Value = ruleset.ConvertToLegacyMods(mods);
        }

        /// <summary>
        /// Refresh per-user mods on every <see cref="MultiplayerClient.RoomUpdated"/> tick.
        /// MultiplayerRoomUser.Mods is an IEnumerable&lt;APIMod&gt; on the room user; we copy it
        /// into the matching <see cref="UserGameplayState"/> so the IPC writer (and the
        /// gameplay overlay) can surface FreeMod / per-user LM choices.
        ///
        /// Skipped if the user is not currently tracked in <see cref="userStates"/>; tracking
        /// is driven by <see cref="startWatchingUser"/> on participating users.
        /// </summary>
        private void updateUserModsFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            foreach (var user in multiplayerClient.Room.Users)
            {
                if (!userStates.TryGetValue(user.UserID, out var existing))
                    continue;

                // Cheap reference-equality skip: MultiplayerRoomUser.Mods is reassigned
                // (not mutated in place) on each server push, so reference equality is
                // a sound proxy for "mod selection has not changed".
                if (ReferenceEquals(existing.Mods, user.Mods))
                    continue;

                userStates[user.UserID] = existing with { Mods = user.Mods.ToList() };
            }
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

            var scores = MultiplayerScoreProjection.CalculateTeamScores(
                multiplayerClient.Room.Users,
                userStates,
                multiplayerClient.Room.Settings.Name);

            Score1.Value = scores.Team1;
            Score2.Value = scores.Team2;
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
                multiplayerClient.UserStateChanged -= onUserStateChanged;
                multiplayerClient.GameplayAborted -= onGameplayAborted;
            }
        }
    }

    public record PendingInvite(long RoomId, string? Password, string InviterName);
}
