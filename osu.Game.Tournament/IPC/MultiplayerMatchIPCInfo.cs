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
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// A <see cref="MatchIPCInfo"/> implementation that sources match data directly from a live
    /// multiplayer room via <see cref="MultiplayerClient"/> and <see cref="SpectatorClient"/>,
    /// replacing the file-based IPC bridge used with the stable client.
    ///
    /// This is a pure connection / data source: it joins a room as a spectator, keeps the
    /// participant watch-set alive, mirrors the room's current beatmap / mods / chat channel onto
    /// the <see cref="MatchIPCInfo"/> bindables, and exposes connection state plus a
    /// <see cref="HasActiveSpectatorPlayers"/> signal. It does not render gameplay (that is the
    /// <c>TournamentSpectatorScreen</c>), derive team scores, or write any IPC snapshot.
    /// </summary>
    public partial class MultiplayerMatchIPCInfo : MatchIPCInfo
    {
        /// <summary>
        /// Delay before <see cref="TourneyState.Ranking"/> auto-resets back to <see cref="TourneyState.Idle"/>.
        /// A multiplayer room has no natural "returned to lobby" event, so anything gated on
        /// <c>State == Idle</c> (e.g. the gameplay-screen auto-advance) hangs off this timer.
        /// </summary>
        public const double RANKING_TO_IDLE_DELAY_MS = 20_000;

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

        /// <summary>
        /// A user-facing error message from the last failed connection attempt, or null if none.
        /// </summary>
        public IBindable<string?> ConnectionError => connectionError;

        private readonly Bindable<string?> connectionError = new Bindable<string?>();

        /// <summary>
        /// Whether any user in the room was already participating in a round (Playing / loading /
        /// ready-for-gameplay) at the moment the connection succeeded. Set before
        /// <see cref="IsConnected"/> flips to <c>true</c> so IsConnected listeners read a
        /// consistent snapshot. Reset on disconnect.
        /// </summary>
        public bool JoinedDuringGameplay { get; private set; }

        /// <summary>
        /// <c>true</c> while at least one watched user is reporting <see cref="SpectatedUserState.Playing"/>.
        /// Driven directly from <see cref="SpectatorClient.WatchedUserStates"/> on this (always-alive,
        /// non-<c>Drawable</c>) component, so the signal fires reliably regardless of which screen is
        /// currently visible — a hidden screen's paused scheduler cannot miss the transition.
        /// </summary>
        public IBindable<bool> HasActiveSpectatorPlayers => hasActiveSpectatorPlayers;

        private readonly Bindable<bool> hasActiveSpectatorPlayers = new Bindable<bool>();

        /// <summary>
        /// The user IDs currently participating in the round (used to construct a spectating display).
        /// Read on the update thread.
        /// </summary>
        public IReadOnlyList<int> CurrentParticipants =>
            multiplayerClient.Room?.Users.Where(u => isParticipatingInCurrentRound(u.State)).Select(u => u.UserID).ToArray()
            ?? Array.Empty<int>();

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

        private readonly IBindableDictionary<int, SpectatorState> watchedUserStates = new BindableDictionary<int, SpectatorState>();

        /// <summary>
        /// The set of user IDs we are currently watching via the spectator client.
        /// </summary>
        private readonly HashSet<int> watchedUsers = new HashSet<int>();

        private string? connectedRoomPassword;
        private int lastBeatmapId;
        private ScheduledDelegate? scheduledRankingReset;

        [BackgroundDependencyLoader]
        private void load()
        {
            watchedUserStates.BindTo(spectatorClient.WatchedUserStates);
            watchedUserStates.BindCollectionChanged((_, _) => recomputeHasActiveSpectatorPlayers(), true);
        }

        private void recomputeHasActiveSpectatorPlayers()
        {
            bool anyPlaying = watchedUserStates.Values.Any(s => s.State == SpectatedUserState.Playing);

            if (hasActiveSpectatorPlayers.Value != anyPlaying)
                hasActiveSpectatorPlayers.Value = anyPlaying;
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

                // JoinRoom and ToggleSpectate access MultiplayerClient.Room which asserts the update
                // thread. Schedule each call separately to guarantee both start on the update thread
                // (JoinRoom uses ConfigureAwait(false) internally, so the continuation after the first
                // await would otherwise land on a thread-pool thread).
                Schedule(() => connectionError.Value = null);
                await scheduleOnUpdateThread(() => multiplayerClient.JoinRoom(apiRoom, password)).ConfigureAwait(false);

                // Switch to spectator mode. Non-fatal if the server rejects it.
                try
                {
                    await scheduleOnUpdateThread(() => multiplayerClient.ToggleSpectate()).ConfigureAwait(false);
                }
                catch (Exception toggleEx)
                {
                    Logger.Log($"[MultiplayerMatchIPCInfo] Failed to toggle spectate (continuing): {toggleEx.GetType().Name}: {toggleEx.Message}",
                        LoggingTarget.Network, LogLevel.Important);
                }

                // Subscribe to events (safe from any thread — just delegate additions).
                multiplayerClient.RoomUpdated += onRoomUpdated;
                multiplayerClient.LoadRequested += onLoadRequested;
                multiplayerClient.GameplayStarted += onGameplayStarted;
                multiplayerClient.ResultsReady += onResultsReady;
                multiplayerClient.UserJoined += onUserJoined;
                multiplayerClient.UserLeft += onUserLeft;
                multiplayerClient.UserKicked += onUserKicked;
                multiplayerClient.UserStateChanged += onUserStateChanged;
                multiplayerClient.GameplayAborted += onGameplayAborted;

                // Start watching users already participating in the current round. Users in
                // Idle/Ready/etc. are picked up via UserStateChanged when they transition in.
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

                string errorMessage = e.ToString().Contains("InvalidPasswordException", StringComparison.Ordinal)
                    ? "Invalid password"
                    : $"Failed to connect: {e.InnerException?.Message ?? e.Message}";

                Schedule(() => connectionError.Value = errorMessage);
                await Disconnect().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disconnects from the current room and reconnects to it after a delay.
        /// </summary>
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
        /// Disconnects from the current multiplayer room.
        /// </summary>
        public async Task Disconnect()
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

                // Stop watching all users on the update thread.
                foreach (int userId in watchedUsers.ToArray())
                    stopWatchingUser(userId);

                isConnected.Value = false;
                connectedRoomId.Value = null;
                connectedRoomPassword = null;
                JoinedDuringGameplay = false;
                lastBeatmapId = 0;

                // Reset the inherited MatchIPCInfo bindables to defaults.
                Beatmap.Value = null;
                Mods.Value = LegacyMods.None;
                State.Value = TourneyState.Idle;
                ChatChannel.Value = string.Empty;
                Score1.Value = 0;
                Score2.Value = 0;

                Logger.Log("[MultiplayerMatchIPCInfo] Disconnected from room", LoggingTarget.Network);
            });
        }

        /// <summary>
        /// Schedules an async operation to start on the update thread and returns a task that
        /// completes when the operation finishes.
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

        private void startWatchingUser(int userId)
        {
            if (!watchedUsers.Add(userId))
                return;

            spectatorClient.WatchUser(userId);
        }

        private void stopWatchingUser(int userId)
        {
            if (!watchedUsers.Remove(userId))
                return;

            spectatorClient.StopWatchingUser(userId);
        }

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
                Logger.Log($"[MultiplayerMatchIPCInfo] onLoadRequested: state {State.Value} -> WaitingForClients", LoggingTarget.Runtime);

                cancelScheduledRankingReset();
                State.Value = TourneyState.WaitingForClients;

                // Clear the previous map's totals; TournamentSpectatorScreen re-derives them once players start.
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
            Schedule(() =>
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] onGameplayStarted: state {State.Value} -> Playing", LoggingTarget.Runtime);
                State.Value = TourneyState.Playing;
            });
        }

        private void onResultsReady()
        {
            Schedule(() =>
            {
                Logger.Log($"[MultiplayerMatchIPCInfo] onResultsReady: state {State.Value} -> Ranking (Idle reset in {RANKING_TO_IDLE_DELAY_MS}ms)", LoggingTarget.Runtime);

                State.Value = TourneyState.Ranking;

                // Auto-restore Idle so consumers gated on lobby state (gameplay-screen auto-advance)
                // get a deterministic "results have been shown long enough, room is back in lobby" signal.
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
                Logger.Log($"[MultiplayerMatchIPCInfo] onGameplayAborted({reason}): state {State.Value} -> Idle", LoggingTarget.Runtime);
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

        private void onUserLeft(MultiplayerRoomUser user) => Schedule(() => stopWatchingUser(user.UserID));

        private void onUserKicked(MultiplayerRoomUser user) => Schedule(() => stopWatchingUser(user.UserID));

        private void onUserStateChanged(MultiplayerRoomUser user, MultiplayerUserState state)
        {
            // Pick up users transitioning into a participating state (e.g. Ready -> WaitingForLoad
            // when the host starts the round). startWatchingUser is idempotent. Watches are NOT torn
            // down on transition out — keeping them alive across
            // play -> FinishedPlay -> Results -> Idle -> Ready -> WaitingForLoad avoids losing the
            // final frame bundle and avoids re-subscription churn. They are released on user-left or
            // disconnect. This keeps bandwidth proportional to slot count, not room size.
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

            // Guard explicitly rather than catching CurrentPlaylistItem's throw, which would mask real errors.
            if (multiplayerClient.Room.Playlist.Count == 0)
                return;

            int beatmapId = multiplayerClient.Room.CurrentPlaylistItem.BeatmapID;

            if (beatmapId == lastBeatmapId)
                return;

            lastBeatmapId = beatmapId;

            // Resolve from the beatmap ID alone (no tournament round-pool dependency). The API
            // lookup populates the SongBar; ensureBeatmapDownloaded makes the beatmap available
            // locally so the spectating display can render it.
            Task.Run(async () =>
            {
                var apiBeatmap = await beatmapLookupCache.GetBeatmapAsync(beatmapId).ConfigureAwait(false);

                if (apiBeatmap == null)
                    return;

                Schedule(() =>
                {
                    if (lastBeatmapId == beatmapId)
                        Beatmap.Value = new TournamentBeatmap(apiBeatmap);
                });

                ensureBeatmapDownloaded(apiBeatmap);
            });
        }

        /// <summary>
        /// Downloads a beatmap set if it is not already available locally or being downloaded.
        /// </summary>
        private void ensureBeatmapDownloaded(APIBeatmap apiBeatmap)
        {
            Schedule(() =>
            {
                if (beatmapManager.QueryBeatmap(b => b.OnlineID == apiBeatmap.OnlineID) != null)
                    return;

                var beatmapSet = apiBeatmap.BeatmapSet;

                if (beatmapSet == null)
                {
                    Logger.Log($"[MultiplayerMatchIPCInfo] Cannot download beatmap {apiBeatmap.OnlineID}: no beatmap set info", LoggingTarget.Network, LogLevel.Important);
                    return;
                }

                if (beatmapDownloader.GetExistingDownload(beatmapSet) != null)
                    return;

                Logger.Log($"[MultiplayerMatchIPCInfo] Downloading beatmap set {beatmapSet.OnlineID}", LoggingTarget.Network);
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

        private void updateChatChannelFromRoom()
        {
            if (multiplayerClient.Room == null)
                return;

            // Raw numeric ChannelID (no "#mp_" prefix). The chat display joins it as
            // ChannelType.Multiplayer, skipping the REST JoinChannelRequest which fails/duplicates
            // for implicitly-joined room channels.
            ChatChannel.Value = multiplayerClient.Room.ChannelID.ToString();
        }

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (spectatorClient.IsNotNull())
            {
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
}
