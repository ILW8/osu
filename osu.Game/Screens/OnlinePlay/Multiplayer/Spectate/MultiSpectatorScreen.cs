// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Spectate;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Screens.OnlinePlay.Multiplayer.Spectate
{
    /// <summary>
    /// A <see cref="SpectatorScreen"/> that spectates multiple users in a match.
    /// </summary>
    public partial class MultiSpectatorScreen : SpectatorScreen
    {
        // Isolates beatmap/ruleset to this screen.
        public override bool DisallowExternalBeatmapRulesetChanges => true;

        // We are managing our own adjustments. For now, this happens inside the Player instances themselves.
        public override bool? ApplyModTrackAdjustments => false;

        public override bool HideOverlaysOnEnter => true;

        /// <summary>
        /// Whether all spectating players have finished loading.
        /// </summary>
        public bool AllPlayersLoaded => instances.All(p => p.PlayerLoaded);

        internal DrawableGameplayLeaderboard Leaderboard { get; private set; } = null!;

        protected override UserActivity InitialActivity => new UserActivity.SpectatingMultiplayerGame(Beatmap.Value.BeatmapInfo, Ruleset.Value);

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Cached(typeof(IGameplayLeaderboardProvider))]
        private MultiSpectatorLeaderboardProvider leaderboardProvider { get; set; }

        private IAggregateAudioAdjustment? boundAdjustments;

        private readonly PlayerArea[] instances;
        private MasterGameplayClockContainer masterClockContainer = null!;
        private SpectatorSyncManager syncManager = null!;
        private PlayerGrid grid = null!;
        private PlayerArea? currentAudioSource;

        private readonly Room room;

        private ReplaySettingsOverlay replaySettingsOverlay = null!;
        private Bindable<bool> configSettingsOverlay = null!;

        /// <summary>
        /// Grace period to wait for a terminal <see cref="SpectatedUserState"/> after the multiplayer hub indicates a user has ended play.
        /// If the spectator stream does not deliver a terminal state within this window, the local replay is forcibly marked complete.
        /// </summary>
        private const double terminal_state_grace_period_ms = 2000;

        private readonly Dictionary<int, ScheduledDelegate> pendingForceTerminations = new Dictionary<int, ScheduledDelegate>();

        /// <summary>
        /// Creates a new <see cref="MultiSpectatorScreen"/>.
        /// </summary>
        /// <param name="room">The room.</param>
        /// <param name="users">The players to spectate.</param>
        public MultiSpectatorScreen(Room room, MultiplayerRoomUser[] users)
            : base(users.Select(u => u.UserID).ToArray())
        {
            this.room = room;

            instances = new PlayerArea[Users.Count];
            leaderboardProvider = new MultiSpectatorLeaderboardProvider(users);
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            configSettingsOverlay = config.GetBindable<bool>(OsuSetting.ReplaySettingsOverlay);

            FillFlowContainer leaderboardFlow;
            Container scoreDisplayContainer;

            InternalChildren = new Drawable[]
            {
                masterClockContainer = new MasterGameplayClockContainer(Beatmap.Value, 0)
                {
                    Child = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                scoreDisplayContainer = new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y
                                },
                            },
                            new Drawable[]
                            {
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    ColumnDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                                    Content = new[]
                                    {
                                        new Drawable[]
                                        {
                                            leaderboardFlow = new FillFlowContainer
                                            {
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(5)
                                            },
                                            grid = new PlayerGrid { RelativeSizeAxes = Axes.Both }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                syncManager = new SpectatorSyncManager(masterClockContainer)
                {
                    ReadyToStart = performInitialSeek,
                },
                replaySettingsOverlay = new ReplaySettingsOverlay
                {
                    Alpha = 0,
                }
            };

            for (int i = 0; i < Users.Count; i++)
            {
                var instance = new PlayerArea(Users[i], syncManager.CreateManagedClock());
                instance.OnShowingResults += () => onPlayerShowingResults(instance);
                grid.Add(instances[i] = instance);
            }

            LoadComponentAsync(leaderboardProvider, _ =>
            {
                AddInternal(leaderboardProvider);
                foreach (var instance in instances)
                    leaderboardProvider.AddClock(instance.UserId, instance.SpectatorPlayerClock);

                if (leaderboardProvider.TeamScores.Count == 2)
                {
                    LoadComponentAsync(new MatchScoreDisplay
                    {
                        Team1Score = { BindTarget = leaderboardProvider.TeamScores.First().Value },
                        Team2Score = { BindTarget = leaderboardProvider.TeamScores.Last().Value },
                    }, scoreDisplayContainer.Add);
                }
            });
            leaderboardFlow.Insert(0, Leaderboard = new DrawableGameplayLeaderboard
            {
                CollapseDuringGameplay = { Value = false },
                AlwaysShown = true,
            });

            LoadComponentAsync(new GameplayChatDisplay(room)
            {
                Expanded = { Value = true },
            }, chat => leaderboardFlow.Insert(1, chat));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            masterClockContainer.Reset();

            // Start with adjustments from the first player to keep a sane state.
            bindAudioAdjustments(instances.First());

            configSettingsOverlay.BindValueChanged(_ => updateVisibility(), true);

            multiplayerClient.UserStateChanged += onMultiplayerUserStateChanged;
        }

        private void updateVisibility()
        {
            if (configSettingsOverlay.Value)
                replaySettingsOverlay.Show();
            else
                replaySettingsOverlay.Hide();
        }

        protected override void Update()
        {
            base.Update();

            checkAudioSource();
        }

        private void checkAudioSource()
        {
            // always use the maximised player instance as the current audio source if there is one
            if (grid.MaximisedCell?.Content is PlayerArea maximisedPlayer && maximisedPlayer == currentAudioSource)
                return;

            // if there is no maximised player instance and the previous audio source is still good to use, keep using it
            if (grid.MaximisedCell == null && isCandidateAudioSource(currentAudioSource?.SpectatorPlayerClock))
                return;

            // at this point we're in one of the following scenarios:
            // - the maximised player instance is not the current audio source => we want to switch to the maximised player instance
            // - there is no maximised player instance, and the previous audio source is stopped => find another running audio source
            currentAudioSource = grid.MaximisedCell?.Content as PlayerArea
                                 ?? instances.Where(i => isCandidateAudioSource(i.SpectatorPlayerClock)).MinBy(i => Math.Abs(i.SpectatorPlayerClock.CurrentTime - syncManager.CurrentMasterTime));

            // Only bind adjustments if there's actually a valid source, else just use the previous ones to ensure no sudden changes to audio.
            if (currentAudioSource != null)
                bindAudioAdjustments(currentAudioSource);

            foreach (var instance in instances)
                instance.Mute = instance != currentAudioSource;
        }

        private void bindAudioAdjustments(PlayerArea first)
        {
            if (boundAdjustments != null)
                masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);

            boundAdjustments = first.ClockAdjustmentsFromMods;
            masterClockContainer.AdjustmentsFromMods.BindAdjustments(boundAdjustments);
        }

        private bool isCandidateAudioSource(SpectatorPlayerClock? clock)
            => clock?.IsRunning == true && !clock.IsCatchingUp && !clock.WaitingOnFrames;

        private void performInitialSeek()
        {
            // We want to start showing gameplay as soon as possible.
            // Each client may be in a different place in the beatmap, so we need to do our best to find a common
            // starting point.
            //
            // Preferring a lower value ensures that we don't have some clients stuttering to keep up.
            List<double> minFrameTimes = new List<double>();

            foreach (var instance in instances)
            {
                if (instance.Score == null)
                    continue;

                minFrameTimes.Add(instance.Score.Replay.Frames.MinBy(f => f.Time)?.Time ?? 0);
            }

            // Remove any outliers (only need to worry about removing those lower than the mean since we will take a Min() after).
            double mean = minFrameTimes.Average();
            minFrameTimes.RemoveAll(t => mean - t > 1000);

            double startTime = minFrameTimes.Min();

            masterClockContainer.Reset(startTime, true);
            Logger.Log($"Multiplayer spectator seeking to initial time of {startTime}");
        }

        protected override void OnNewPlayingUserState(int userId, SpectatorState spectatorState)
        {
        }

        protected override void StartGameplay(int userId, SpectatorGameplayState spectatorGameplayState) => Schedule(() =>
        {
            var playerArea = instances.Single(i => i.UserId == userId);

            // The multiplayer spectator flow requires the client to return to a higher level screen
            // (ie. StartGameplay should only be called once per player).
            //
            // Meanwhile, the solo spectator flow supports multiple `StartGameplay` calls.
            // To ensure we don't crash out in an edge case where this is called more than once in multiplayer,
            // guard against re-entry for the same player.
            if (playerArea.Score != null)
                return;

            playerArea.LoadScore(spectatorGameplayState.Score);
        });

        protected override void FailGameplay(int userId) => Schedule(() =>
        {
            // Deferring sync-manager unregistration until the inner Player actually transitions to results
            // (see `onPlayerShowingResults`). Removing the managed clock here freezes the per-player clock,
            // which strands local replay playback when the spectator hasn't yet caught up to the end of the
            // received frames — in that case `ScoreProcessor.HasCompleted` never fires and the results
            // screen never appears.
        });

        protected override void PassGameplay(int userId) => Schedule(() =>
        {
            // See `FailGameplay` for the rationale behind deferring clock cleanup.
        });

        protected override void QuitGameplay(int userId) => Schedule(() =>
        {
            RemoveUser(userId);

            var instance = instances.Single(i => i.UserId == userId);

            instance.FadeColour(colours.Gray4, 400, Easing.OutQuint);
            syncManager.RemoveManagedClock(instance.SpectatorPlayerClock);
        });

        public override bool OnBackButton()
        {
            if (multiplayerClient.Room == null)
                return base.OnBackButton();

            // On a manual exit, set the player back to idle unless gameplay has finished.
            // Of note, this doesn't cover exiting using alt-f4 or menu home option.
            if (multiplayerClient.Room.State != MultiplayerRoomState.Open)
                multiplayerClient.ChangeState(MultiplayerUserState.Idle).FireAndForget();

            return base.OnBackButton();
        }

        /// <summary>
        /// Invoked when a <see cref="PlayerArea"/>'s inner <see cref="MultiSpectatorPlayer"/> is about to push its results screen.
        /// At this point local gameplay has run to completion and it is safe to detach the per-player clock from the sync manager.
        /// </summary>
        private void onPlayerShowingResults(PlayerArea instance) => Schedule(() =>
        {
            Logger.Log($"Player area for user {instance.UserId} is showing results; releasing managed clock.");
            syncManager.RemoveManagedClock(instance.SpectatorPlayerClock);
        });

        /// <summary>
        /// The multiplayer hub is authoritative on whether a user has ended play. The spectator hub occasionally fails to deliver
        /// the matching terminal <see cref="SpectatedUserState"/>, leaving the corresponding <see cref="PlayerArea"/> stuck on
        /// <see cref="SpectatorPlayerClock.WaitingOnFrames"/> with no transition to results. When the multiplayer hub reports a
        /// transition to <see cref="MultiplayerUserState.FinishedPlay"/> or <see cref="MultiplayerUserState.Results"/>, start a
        /// grace timer; if no terminal spectator state arrives within the window, locally mark the replay complete so the natural
        /// completion flow inside <see cref="MultiSpectatorPlayer"/> can run.
        /// </summary>
        private void onMultiplayerUserStateChanged(MultiplayerRoomUser user, MultiplayerUserState newState) => Schedule(() =>
        {
            int userId = user.UserID;

            // If the user has somehow regressed to a pre-completion state, cancel any pending force-termination.
            if (newState < MultiplayerUserState.FinishedPlay)
            {
                cancelPendingForceTermination(userId);
                return;
            }

            if (newState != MultiplayerUserState.FinishedPlay && newState != MultiplayerUserState.Results)
                return;

            var playerArea = instances.FirstOrDefault(i => i.UserId == userId);
            if (playerArea == null)
                return;

            // Already scheduled — do not double up.
            if (pendingForceTerminations.ContainsKey(userId))
                return;

            pendingForceTerminations[userId] = Scheduler.AddDelayed(() => forceTerminateIfStuck(userId, playerArea), terminal_state_grace_period_ms);
        });

        private void cancelPendingForceTermination(int userId)
        {
            if (!pendingForceTerminations.TryGetValue(userId, out var pending))
                return;

            pending.Cancel();
            pendingForceTerminations.Remove(userId);
        }

        private void forceTerminateIfStuck(int userId, PlayerArea playerArea)
        {
            pendingForceTerminations.Remove(userId);

            // Nothing to do if a terminal spectator state arrived during the grace window, or if the player never started.
            if (playerArea.Score == null || playerArea.Score.Replay.HasReceivedAllFrames)
                return;

            Logger.Log($"Spectator stream did not deliver a terminal state for user {userId} within {terminal_state_grace_period_ms}ms of multiplayer-side end-of-play; forcing replay completion to unblock results.");

            // Unblocks FramedReplayInputHandler.WaitingForFrame so the per-player clock can advance past the last received frame,
            // remaining hit objects judge (auto-missing if frames were also dropped), ScoreProcessor.HasCompleted fires, and the
            // existing Player.progressToResults path pushes MultiSpectatorResultsScreen as usual.
            playerArea.Score.Replay.HasReceivedAllFrames = true;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (multiplayerClient.IsNotNull())
                multiplayerClient.UserStateChanged -= onMultiplayerUserStateChanged;

            foreach (var pending in pendingForceTerminations.Values)
                pending.Cancel();
            pendingForceTerminations.Clear();
        }
    }
}
