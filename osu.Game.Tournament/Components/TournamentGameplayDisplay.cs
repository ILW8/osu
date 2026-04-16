// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Spectator;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Screens.Spectate;
using osu.Game.Tournament.IPC;
using Realms;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Embeds actual gameplay rendering from a multiplayer room into the tournament overlay.
    /// Displays two <see cref="PlayerArea"/> instances side by side (one per team).
    /// Manages the spectator lifecycle: watches user states, resolves beatmaps,
    /// creates scores, and handles clock synchronization.
    /// </summary>
    public partial class TournamentGameplayDisplay : CompositeDrawable
    {
        [Resolved]
        private SpectatorClient spectatorClient { get; set; } = null!;

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesetStore { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        private readonly MultiplayerMatchIPCInfo multiplayerIpc;

        /// <summary>
        /// The outer container holding all gameplay-related drawables.
        /// Recreated each time gameplay starts.
        /// </summary>
        private Container gameplayContainer = null!;

        /// <summary>
        /// Container for <see cref="PlayerArea"/>s. A child of <see cref="masterClockContainer"/>
        /// so that the master clock's <see cref="IGameplayClock"/> is in the DI chain
        /// (matching <see cref="MultiSpectatorScreen"/>'s hierarchy).
        /// </summary>
        private Container playerAreasContainer = null!;

        private MasterGameplayClockContainer? masterClockContainer;
        private SpectatorSyncManager? syncManager;

        /// <summary>
        /// Player areas keyed by user ID. Created on-demand when a player starts gameplay,
        /// so the sync manager only tracks clocks that have actual scores loaded.
        /// </summary>
        private readonly Dictionary<int, PlayerArea> playerAreas = new Dictionary<int, PlayerArea>();

        private readonly IBindableDictionary<int, SpectatorState> watchedStates = new BindableDictionary<int, SpectatorState>();
        private readonly Dictionary<int, SpectatorGameplayState> gameplayStates = new Dictionary<int, SpectatorGameplayState>();

        /// <summary>
        /// The player area currently providing audio.
        /// </summary>
        private PlayerArea? currentAudioSource;

        private IAggregateAudioAdjustment? boundAdjustments;

        private IDisposable? realmSubscription;
        private bool gameplayActive;

        public TournamentGameplayDisplay(MultiplayerMatchIPCInfo multiplayerIpc)
        {
            this.multiplayerIpc = multiplayerIpc;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = gameplayContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
            };

            // Watch for beatmap downloads completing so we can start gameplay.
            realmSubscription = realm.RegisterForNotifications(
                r => r.All<BeatmapSetInfo>().Where(s => !s.DeletePending), beatmapsChanged);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            watchedStates.BindTo(spectatorClient.WatchedUserStates);
            watchedStates.BindCollectionChanged(onWatchedStatesChanged, true);

            multiplayerIpc.IsConnected.BindValueChanged(connected =>
            {
                if (!connected.NewValue)
                    teardownGameplay();
            });
        }

        protected override void Update()
        {
            base.Update();

            if (gameplayActive)
                updateAudioSource();
        }

        private void onWatchedStatesChanged(object? sender, NotifyDictionaryChangedEventArgs<int, SpectatorState> e)
        {
            switch (e.Action)
            {
                case NotifyDictionaryChangedAction.Add:
                case NotifyDictionaryChangedAction.Replace:
                    foreach ((int userId, SpectatorState state) in e.NewItems.AsNonNull())
                        onUserStateChanged(userId, state);
                    break;
            }
        }

        private void onUserStateChanged(int userId, SpectatorState newState)
        {
            if (newState.RulesetID == null || newState.BeatmapID == null)
                return;

            // Only process users from our room.
            if (multiplayerClient.Room == null)
                return;

            if (multiplayerClient.Room.Users.All(u => u.UserID != userId))
                return;

            switch (newState.State)
            {
                case SpectatedUserState.Playing:
                    tryStartGameplay(userId);
                    break;

                case SpectatedUserState.Passed:
                    onPlayerFinished(userId);
                    break;

                case SpectatedUserState.Failed:
                    onPlayerFinished(userId);
                    break;

                case SpectatedUserState.Quit:
                    onPlayerFinished(userId);
                    break;
            }
        }

        private void onPlayerFinished(int userId)
        {
            if (gameplayStates.TryGetValue(userId, out var state))
                state.Score.Replay.HasReceivedAllFrames = true;

            gameplayStates.Remove(userId);

            // Remove the clock from the sync manager so it doesn't block other players.
            if (playerAreas.TryGetValue(userId, out var area) && syncManager != null)
                syncManager.RemoveManagedClock(area.SpectatorPlayerClock);
        }

        private void tryStartGameplay(int userId)
        {
            if (!watchedStates.TryGetValue(userId, out var spectatorState))
                return;

            if (gameplayStates.ContainsKey(userId))
                return;

            // Get user info from the multiplayer room — already populated by MultiplayerClient.JoinRoom.
            // This avoids an async lookup that would delay PlayerArea creation and cause sync gaps.
            var roomUser = multiplayerClient.Room?.Users.FirstOrDefault(u => u.UserID == userId);
            var user = roomUser?.User;

            if (user == null)
                return;

            var resolvedRuleset = rulesetStore.AvailableRulesets.FirstOrDefault(r => r.OnlineID == spectatorState.RulesetID)?.CreateInstance();
            if (resolvedRuleset == null)
                return;

            var resolvedBeatmap = beatmapManager.QueryBeatmap(b => b.OnlineID == spectatorState.BeatmapID);

            if (resolvedBeatmap == null)
            {
                Logger.Log($"[TournamentGameplayDisplay] Beatmap {spectatorState.BeatmapID} not locally available for user {userId}. Waiting for download...", LoggingTarget.Runtime);
                return;
            }

            var score = new Score
            {
                ScoreInfo = new ScoreInfo
                {
                    BeatmapInfo = resolvedBeatmap,
                    User = user,
                    Mods = spectatorState.Mods.Select(m => m.ToMod(resolvedRuleset)).ToArray(),
                    Ruleset = resolvedRuleset.RulesetInfo,
                },
                Replay = new Replay { HasReceivedAllFrames = false },
            };

            var gameplayState = new SpectatorGameplayState(score, resolvedRuleset, beatmapManager.GetWorkingBeatmap(resolvedBeatmap));
            gameplayStates[userId] = gameplayState;

            Logger.Log($"[TournamentGameplayDisplay] Starting gameplay for user {userId} ({user.Username})", LoggingTarget.Runtime);
            loadUserIntoPlayerArea(userId, gameplayState);
        }

        private void loadUserIntoPlayerArea(int userId, SpectatorGameplayState gameplayState)
        {
            // Ensure master clock + sync manager are set up (created once per gameplay session).
            if (masterClockContainer == null)
                setupGameplayInfrastructure(gameplayState.Beatmap);

            Debug.Assert(syncManager != null);

            // Don't create a second area for this user.
            if (playerAreas.ContainsKey(userId))
                return;

            // Determine which side this user should be on based on team.
            var roomUser = multiplayerClient.Room?.Users.FirstOrDefault(u => u.UserID == userId);
            bool isTeamRed = roomUser?.MatchState is TeamVersusUserState teamState && teamState.TeamID == 0;

            // Check if the target side already has a player.
            bool sideOccupied = playerAreas.Values.Any(a =>
            {
                bool areaIsLeft = a.Anchor == Anchor.TopLeft;
                return areaIsLeft == isTeamRed;
            });

            if (sideOccupied)
                return;

            // Create managed clock and PlayerArea on-demand so the sync manager
            // only tracks clocks that have actual scores to play.
            var playerArea = new PlayerArea(userId, syncManager.CreateManagedClock())
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                Anchor = isTeamRed ? Anchor.TopLeft : Anchor.TopRight,
                Origin = isTeamRed ? Anchor.TopLeft : Anchor.TopRight,
            };

            playerAreas[userId] = playerArea;
            playerAreasContainer.Add(playerArea);

            playerArea.LoadScore(gameplayState.Score);

            // Bind audio adjustments from the first loaded player to keep the master clock in sync.
            if (boundAdjustments == null)
                bindAudioAdjustments(playerArea);
        }

        private void setupGameplayInfrastructure(WorkingBeatmap workingBeatmap)
        {
            teardownGameplay();

            gameplayActive = true;

            // MasterGameplayClockContainer accesses the track in its constructor.
            if (!workingBeatmap.TrackLoaded)
                workingBeatmap.LoadTrack();

            playerAreasContainer = new Container { RelativeSizeAxes = Axes.Both };

            masterClockContainer = new MasterGameplayClockContainer(workingBeatmap, 0)
            {
                // PlayerAreas are children of the master clock container so that the master's
                // IGameplayClock is in their DI chain (matching MultiSpectatorScreen's hierarchy).
                Child = playerAreasContainer,
            };

            syncManager = new SpectatorSyncManager(masterClockContainer)
            {
                ReadyToStart = performInitialSeek,
            };

            gameplayContainer.Children = new Drawable[]
            {
                masterClockContainer,
                syncManager,
            };

            // Reset the master clock but don't start it yet —
            // performInitialSeek will seek and start once player clocks have frames.
            masterClockContainer.Reset();
        }

        private void teardownGameplay()
        {
            if (!gameplayActive)
                return;

            gameplayActive = false;
            gameplayStates.Clear();

            if (syncManager != null)
            {
                foreach (var area in playerAreas.Values)
                    syncManager.RemoveManagedClock(area.SpectatorPlayerClock);
            }

            if (boundAdjustments != null && masterClockContainer != null)
                masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);

            boundAdjustments = null;
            currentAudioSource = null;

            playerAreas.Clear();
            gameplayContainer.Clear();
            masterClockContainer = null;
            syncManager = null;
        }

        /// <summary>
        /// Seeks the master clock to the earliest available frame time across all loaded players
        /// and starts playback. Mirrors <see cref="MultiSpectatorScreen"/>'s initial seek logic.
        /// </summary>
        private void performInitialSeek()
        {
            Debug.Assert(masterClockContainer != null);

            var minFrameTimes = new List<double>();

            foreach (var area in playerAreas.Values)
            {
                if (area.Score == null)
                    continue;

                var minFrame = area.Score.Replay.Frames.MinBy(f => f.Time);

                if (minFrame != null)
                    minFrameTimes.Add(minFrame.Time);
            }

            if (minFrameTimes.Count == 0)
            {
                masterClockContainer.Reset(0, true);
                return;
            }

            // Remove outliers — if one player's earliest frame is >1s behind the mean,
            // exclude it to avoid seeking too far back.
            double mean = minFrameTimes.Average();
            minFrameTimes.RemoveAll(t => mean - t > 1000);

            double startTime = minFrameTimes.Count > 0 ? minFrameTimes.Min() : 0;

            Logger.Log($"[TournamentGameplayDisplay] Seeking to initial time {startTime:N0}ms", LoggingTarget.Runtime);
            masterClockContainer.Reset(startTime, true);

        }

        #region Audio source management

        /// <summary>
        /// Selects the best audio source each frame (matching <see cref="MultiSpectatorScreen"/>'s logic)
        /// and ensures mod rate adjustments are bound to the master clock.
        /// </summary>
        private void updateAudioSource()
        {
            if (syncManager == null || masterClockContainer == null)
                return;

            // If the current source is still viable, keep using it.
            if (isCandidateAudioSource(currentAudioSource?.SpectatorPlayerClock))
                return;

            // Pick the running player clock closest to the master clock time.
            currentAudioSource = playerAreas.Values
                                            .Where(a => isCandidateAudioSource(a.SpectatorPlayerClock))
                                            .MinBy(a => Math.Abs(a.SpectatorPlayerClock.CurrentTime - syncManager.CurrentMasterTime));

            if (currentAudioSource != null)
                bindAudioAdjustments(currentAudioSource);

            foreach (var area in playerAreas.Values)
                area.Mute = area != currentAudioSource;
        }

        private void bindAudioAdjustments(PlayerArea source)
        {
            if (boundAdjustments != null && masterClockContainer != null)
                masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);

            boundAdjustments = source.ClockAdjustmentsFromMods;
            masterClockContainer?.AdjustmentsFromMods.BindAdjustments(boundAdjustments);
        }

        private static bool isCandidateAudioSource(SpectatorPlayerClock? clock)
            => clock?.IsRunning == true && !clock.IsCatchingUp && !clock.WaitingOnFrames;

        #endregion

        private void beatmapsChanged(IRealmCollection<BeatmapSetInfo> items, ChangeSet? changes)
        {
            if (changes?.InsertedIndices == null)
                return;

            if (multiplayerClient.Room == null)
                return;

            // When a beatmap is downloaded, try to start gameplay for any users waiting on it.
            foreach (int c in changes.InsertedIndices)
            {
                var beatmapSet = items[c];

                foreach (var user in multiplayerClient.Room.Users)
                {
                    int userId = user.UserID;

                    if (gameplayStates.ContainsKey(userId))
                        continue;

                    if (!watchedStates.TryGetValue(userId, out var state))
                        continue;

                    if (state.State != SpectatedUserState.Playing)
                        continue;

                    if (beatmapSet.Beatmaps.Any(b => b.OnlineID == state.BeatmapID))
                    {
                        Schedule(() => tryStartGameplay(userId));
                    }
                }
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();
        }
    }
}
