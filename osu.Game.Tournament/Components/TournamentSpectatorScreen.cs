// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Spectator;
using osu.Game.Screens.OnlinePlay.Multiplayer.Spectate;
using osu.Game.Screens.Play;
using osu.Game.Screens.Spectate;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// A <see cref="SpectatorScreen"/> that renders all spectated players of a multiplayer round
    /// simultaneously, embedded in the tournament overlay's gameplay area.
    ///
    /// It inherits the upstream watch → state → gameplay pipeline from <see cref="SpectatorScreen"/>
    /// and composes the same building blocks <see cref="MultiSpectatorScreen"/> uses
    /// (<see cref="MasterGameplayClockContainer"/> + <see cref="SpectatorSyncManager"/> +
    /// <see cref="PlayerArea"/> tiles in a <see cref="TournamentPlayerGrid"/>) — but without the
    /// results screen / leaderboard / chat chrome that would fight the embedded chroma layout.
    /// </summary>
    public partial class TournamentSpectatorScreen : SpectatorScreen
    {
        // Isolate the beatmap/ruleset to this screen so the overlay's global selection isn't disturbed.
        public override bool DisallowExternalBeatmapRulesetChanges => true;

        // Audio adjustments are managed per-player below.
        public override bool? ApplyModTrackAdjustments => false;

        /// <summary>
        /// The number of tiles shown in the grid. Defaults to the participant count (clamped to the
        /// grid's slot range); operator-tunable.
        /// </summary>
        public readonly BindableInt VisibleSlotCount = new BindableInt(TournamentPlayerGrid.MIN_SLOTS)
        {
            MinValue = TournamentPlayerGrid.MIN_SLOTS,
            MaxValue = TournamentPlayerGrid.MAX_SLOTS,
        };

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        private MasterGameplayClockContainer masterClockContainer = null!;
        private SpectatorSyncManager syncManager = null!;
        private TournamentPlayerGrid grid = null!;

        private readonly Dictionary<int, PlayerArea> playerAreas = new Dictionary<int, PlayerArea>();
        private readonly Dictionary<int, int> slots = new Dictionary<int, int>(); // userId -> slot index

        private PlayerArea? currentAudioSource;
        private IAggregateAudioAdjustment? boundAdjustments;

        public TournamentSpectatorScreen(int[] users)
            : base(users)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                // PlayerArea tiles are nested under the master clock container so their
                // SpectatorPlayerClocks resolve the correct IGameplayClock via DI.
                masterClockContainer = new MasterGameplayClockContainer(Beatmap.Value, 0)
                {
                    Child = grid = new TournamentPlayerGrid { RelativeSizeAxes = Axes.Both },
                },
                // The sync manager is a fire-and-forget sibling (NOT nested under the clock container);
                // its Update() drives per-player catch-up and the master clock state.
                syncManager = new SpectatorSyncManager(masterClockContainer)
                {
                    ReadyToStart = performInitialSeek,
                },
            };

            VisibleSlotCount.Value = System.Math.Clamp(Users.Count, TournamentPlayerGrid.MIN_SLOTS, TournamentPlayerGrid.MAX_SLOTS);
            grid.Capacity.BindTo(VisibleSlotCount);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            masterClockContainer.Reset();
        }

        protected override void OnNewPlayingUserState(int userId, SpectatorState spectatorState)
        {
        }

        protected override void StartGameplay(int userId, SpectatorGameplayState spectatorGameplayState) => Schedule(() =>
        {
            if (playerAreas.ContainsKey(userId))
                return;

            // Snapshot the round's participant → slot mapping once, on the first player to start.
            if (slots.Count == 0)
                snapshotSlotsFromRoom();

            int slot = assignSlot(userId);

            if (slot >= TournamentPlayerGrid.MAX_SLOTS)
            {
                Logger.Log($"[TournamentSpectator] user {userId} exceeds the {TournamentPlayerGrid.MAX_SLOTS}-slot grid; not rendering a tile.", level: LogLevel.Important);
                return;
            }

            var area = new PlayerArea(userId, syncManager.CreateManagedClock(), showFailingLayer: false);
            playerAreas[userId] = area;
            grid.Add(area, slot);
            area.LoadScore(spectatorGameplayState.Score);
        });

        protected override void PassGameplay(int userId) => Schedule(() => removeClock(userId));

        protected override void FailGameplay(int userId) => Schedule(() => removeClock(userId));

        protected override void QuitGameplay(int userId) => Schedule(() =>
        {
            RemoveUser(userId);
            removeClock(userId);
        });

        private void removeClock(int userId)
        {
            if (playerAreas.TryGetValue(userId, out var area))
                syncManager.RemoveManagedClock(area.SpectatorPlayerClock);
        }

        protected override void Update()
        {
            base.Update();
            checkAudioSource();
        }

        /// <summary>
        /// Picks a single player tile to act as the audio source and mutes the rest, preferring the
        /// running, in-sync clock closest to the master time. Avoids a cacophony of overlapping audio.
        /// </summary>
        private void checkAudioSource()
        {
            // Keep the current source if it's still a good candidate.
            if (currentAudioSource != null && isCandidateAudioSource(currentAudioSource.SpectatorPlayerClock))
                return;

            currentAudioSource = playerAreas.Values
                                            .Where(a => isCandidateAudioSource(a.SpectatorPlayerClock))
                                            .MinBy(a => Math.Abs(a.SpectatorPlayerClock.CurrentTime - syncManager.CurrentMasterTime));

            // Only rebind if a valid source exists; otherwise keep the previous adjustments to avoid sudden audio changes.
            if (currentAudioSource != null)
                bindAudioAdjustments(currentAudioSource);

            foreach (var area in playerAreas.Values)
                area.Mute = area != currentAudioSource;
        }

        private void bindAudioAdjustments(PlayerArea source)
        {
            if (boundAdjustments != null)
                masterClockContainer.AdjustmentsFromMods.UnbindAdjustments(boundAdjustments);

            boundAdjustments = source.ClockAdjustmentsFromMods;
            masterClockContainer.AdjustmentsFromMods.BindAdjustments(boundAdjustments);
        }

        private static bool isCandidateAudioSource(SpectatorPlayerClock? clock)
            => clock?.IsRunning == true && !clock.IsCatchingUp && !clock.WaitingOnFrames;

        private void snapshotSlotsFromRoom()
        {
            var roomUsers = multiplayerClient.Room?.Users.Select(u => (u.UserID, u.State))
                            ?? Enumerable.Empty<(int, MultiplayerUserState)>();

            foreach ((int userId, int slot) in SnapshotSlots(roomUsers))
                slots[userId] = slot;
        }

        private int assignSlot(int userId)
        {
            if (slots.TryGetValue(userId, out int existing))
                return existing;

            // Fallback for a participant that started gameplay but wasn't in the snapshot (appends after it).
            return slots[userId] = slots.Count;
        }

        /// <summary>
        /// Projects a room's users onto a stable, gap-free slot map, including only users in an
        /// active gameplay state (so Idle/Ready/Spectating users — including the tourney client
        /// itself — don't reserve a tile). Slots are assigned sequentially in input order.
        /// </summary>
        internal static Dictionary<int, int> SnapshotSlots(IEnumerable<(int userId, MultiplayerUserState state)> roomUsers)
        {
            var result = new Dictionary<int, int>();
            int next = 0;

            foreach ((int userId, MultiplayerUserState state) in roomUsers)
            {
                if (!IsParticipating(state))
                    continue;

                result[userId] = next++;
            }

            return result;
        }

        internal static bool IsParticipating(MultiplayerUserState state)
            => state == MultiplayerUserState.WaitingForLoad
               || state == MultiplayerUserState.Loaded
               || state == MultiplayerUserState.ReadyForGameplay
               || state == MultiplayerUserState.Playing;

        private void performInitialSeek()
        {
            // Each client may be at a different point in the beatmap; find a common, low starting point
            // so no client has to stutter to catch up.
            var minFrameTimes = playerAreas.Values
                                           .Where(a => a.Score != null)
                                           .Select(a => a.Score!.Replay.Frames.MinBy(f => f.Time)?.Time ?? 0)
                                           .ToList();

            double startTime = ComputeInitialSeekTime(minFrameTimes);
            masterClockContainer.Reset(startTime, true);
            Logger.Log($"[TournamentSpectator] initial seek to {startTime}");
        }

        /// <summary>
        /// Computes the initial master-clock seek time: trim low outliers (more than 1000ms below the
        /// mean) then take the minimum of the rest. Returns 0 for an empty input.
        /// </summary>
        internal static double ComputeInitialSeekTime(IEnumerable<double> minFrameTimes)
        {
            var times = minFrameTimes.ToList();

            if (times.Count == 0)
                return 0;

            double mean = times.Average();
            times.RemoveAll(t => mean - t > 1000);
            return times.Min();
        }
    }
}
