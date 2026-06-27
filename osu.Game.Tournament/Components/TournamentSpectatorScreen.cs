// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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

        private MasterGameplayClockContainer masterClockContainer = null!;
        private SpectatorSyncManager syncManager = null!;
        private TournamentPlayerGrid grid = null!;

        private readonly Dictionary<int, PlayerArea> playerAreas = new Dictionary<int, PlayerArea>();
        private readonly Dictionary<int, int> slots = new Dictionary<int, int>(); // userId -> slot index

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

            int slot = assignSlot(userId);

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

        // Sequential slot assignment. Replaced by a participation-aware snapshot in a later task.
        private int assignSlot(int userId)
        {
            if (slots.TryGetValue(userId, out int existing))
                return existing;

            return slots[userId] = slots.Count;
        }

        private void performInitialSeek()
        {
            masterClockContainer.Reset(0, true);
        }
    }
}
