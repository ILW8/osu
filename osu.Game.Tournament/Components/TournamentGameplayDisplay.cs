// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
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
        private UserLookupCache userLookupCache { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        private readonly MultiplayerMatchIPCInfo multiplayerIpc;

        /// <summary>
        /// The container holding the gameplay infrastructure (clock container, player areas).
        /// Recreated each time gameplay starts.
        /// </summary>
        private Container gameplayContainer = null!;

        private MasterGameplayClockContainer? masterClockContainer;
        private SpectatorSyncManager? syncManager;

        private PlayerArea? leftArea;
        private PlayerArea? rightArea;

        private readonly IBindableDictionary<int, SpectatorState> watchedStates = new BindableDictionary<int, SpectatorState>();
        private readonly Dictionary<int, APIUser> userMap = new Dictionary<int, APIUser>();
        private readonly Dictionary<int, SpectatorGameplayState> gameplayStates = new Dictionary<int, SpectatorGameplayState>();

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
                    ensureUserPopulated(userId, () => tryStartGameplay(userId));
                    break;

                case SpectatedUserState.Passed:
                    if (gameplayStates.TryGetValue(userId, out var passState))
                        passState.Score.Replay.HasReceivedAllFrames = true;
                    break;

                case SpectatedUserState.Failed:
                case SpectatedUserState.Quit:
                    if (gameplayStates.TryGetValue(userId, out var endState))
                        endState.Score.Replay.HasReceivedAllFrames = true;

                    gameplayStates.Remove(userId);
                    break;
            }
        }

        private void ensureUserPopulated(int userId, Action onComplete)
        {
            if (userMap.ContainsKey(userId))
            {
                onComplete();
                return;
            }

            userLookupCache.GetUserAsync(userId).ContinueWith(task =>
            {
                var user = task.GetResultSafely();

                if (user != null)
                {
                    Schedule(() =>
                    {
                        userMap[userId] = user;
                        onComplete();
                    });
                }
            });
        }

        private void tryStartGameplay(int userId)
        {
            if (!watchedStates.TryGetValue(userId, out var spectatorState))
                return;

            if (!userMap.TryGetValue(userId, out var user))
                return;

            if (gameplayStates.ContainsKey(userId))
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
            // Ensure gameplay infrastructure is set up.
            if (masterClockContainer == null)
                setupGameplayInfrastructure(gameplayState.Beatmap);

            Debug.Assert(syncManager != null);
            Debug.Assert(leftArea != null);
            Debug.Assert(rightArea != null);

            // Determine which side this user should be on based on team.
            var roomUser = multiplayerClient.Room?.Users.FirstOrDefault(u => u.UserID == userId);
            bool isTeamRed = roomUser?.MatchState is TeamVersusUserState teamState && teamState.TeamID == 0;

            var targetArea = isTeamRed ? leftArea : rightArea;

            // Only load if this area doesn't already have a score loaded.
            if (targetArea.Score != null)
                return;

            targetArea.LoadScore(gameplayState.Score);
        }

        private void setupGameplayInfrastructure(WorkingBeatmap workingBeatmap)
        {
            teardownGameplay();

            gameplayActive = true;

            masterClockContainer = new MasterGameplayClockContainer(workingBeatmap, 0);

            syncManager = new SpectatorSyncManager(masterClockContainer)
            {
                ReadyToStart = () =>
                {
                    Logger.Log("[TournamentGameplayDisplay] Clocks ready, starting master clock", LoggingTarget.Runtime);
                    masterClockContainer.Reset(startClock: true);
                }
            };

            leftArea = new PlayerArea(0, syncManager.CreateManagedClock())
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
            };

            rightArea = new PlayerArea(0, syncManager.CreateManagedClock())
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
            };

            // Mute the right player, audio from left only.
            rightArea.Mute = true;
            leftArea.Mute = false;

            gameplayContainer.Children = new Drawable[]
            {
                masterClockContainer,
                syncManager,
                leftArea,
                rightArea,
            };

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
                if (leftArea != null)
                    syncManager.RemoveManagedClock(leftArea.SpectatorPlayerClock);
                if (rightArea != null)
                    syncManager.RemoveManagedClock(rightArea.SpectatorPlayerClock);
            }

            gameplayContainer.Clear();
            masterClockContainer = null;
            syncManager = null;
            leftArea = null;
            rightArea = null;
        }

        private void beatmapsChanged(IRealmCollection<BeatmapSetInfo> items, ChangeSet? changes)
        {
            if (changes?.InsertedIndices == null)
                return;

            // When a beatmap is downloaded, try to start gameplay for any users waiting on it.
            foreach (int c in changes.InsertedIndices)
            {
                var beatmapSet = items[c];

                foreach ((int userId, _) in userMap)
                {
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
