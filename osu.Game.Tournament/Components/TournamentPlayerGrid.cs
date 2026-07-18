// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// A two-team spectator grid of 2–8 tiles. The Red team fills the left half of the grid and
    /// the Blue team the right half, each arranged in a fixed shape keyed by the players-per-team
    /// count (see <see cref="SlotBounds"/>). Tile positions are addressed by a stable slot index —
    /// slots [0, N) are Red, [N, 2N) are Blue — so an absent player leaves a hole rather than
    /// shifting its team-mates. Each tile is wrapped in a masking cell so neighbouring tiles never
    /// bleed across cell boundaries.
    /// </summary>
    public partial class TournamentPlayerGrid : CompositeDrawable
    {
        public const int MIN_SLOTS = 2;
        public const int MAX_SLOTS = 8;

        public BindableInt Capacity { get; } = new BindableInt(MIN_SLOTS)
        {
            MinValue = MIN_SLOTS,
            MaxValue = MAX_SLOTS,
        };

        private readonly Container?[] slotContainers = new Container?[MAX_SLOTS];
        private readonly Container content;

        public TournamentPlayerGrid()
        {
            InternalChild = content = new Container { RelativeSizeAxes = Axes.Both };
        }

        public void Add(Drawable tile, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
                throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index must be in [0, {MAX_SLOTS}).");

            if (slotContainers[slotIndex] != null)
                throw new InvalidOperationException($"Slot {slotIndex} is already occupied.");

            var cell = new Container
            {
                Child = tile,
                Masking = true,
            };

            slotContainers[slotIndex] = cell;
            content.Add(cell);
        }

        public void Remove(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
                return;

            var cell = slotContainers[slotIndex];

            if (cell == null)
                return;

            slotContainers[slotIndex] = null;
            content.Remove(cell, disposeImmediately: true);
        }

        public void Clear()
        {
            for (int i = 0; i < MAX_SLOTS; i++)
                slotContainers[i] = null;

            content.Clear(disposeChildren: true);
        }

        protected override void Update()
        {
            base.Update();

            // Capacity is two teams' worth of PlayersPerTeam, so N is half of it.
            int playersPerTeam = Capacity.Value / 2;

            for (int i = 0; i < MAX_SLOTS; i++)
            {
                var cell = slotContainers[i];

                if (cell == null)
                    continue;

                if (i >= Capacity.Value || playersPerTeam < 1)
                {
                    cell.Alpha = 0;
                    continue;
                }

                var bounds = SlotBounds(i, playersPerTeam);

                cell.Alpha = 1;
                cell.Size = new Vector2(bounds.Width * DrawWidth, bounds.Height * DrawHeight);
                cell.Position = new Vector2(bounds.X * DrawWidth, bounds.Y * DrawHeight);
            }
        }

        /// <summary>
        /// Maps a slot index onto its normalised bounds (origin top-left, y down, values in [0, 1])
        /// for a round with <paramref name="playersPerTeam"/> (N) players per team. Slots [0, N) are
        /// the Red team, filling the left half; [N, 2N) are Blue, filling the right half. Within a
        /// half the tiles take a fixed same-size arrangement: N=1 fills the half, N=2 stacks two
        /// rows, N=3 is a pyramid (one tile centred on top, two below), N=4 is a 2×2. Members fill
        /// their half in slot order (top/top-left first).
        /// </summary>
        internal static RectangleF SlotBounds(int slotIndex, int playersPerTeam)
        {
            bool left = slotIndex < playersPerTeam;
            int withinTeam = left ? slotIndex : slotIndex - playersPerTeam;
            float xOrigin = left ? 0f : 0.5f;

            // Bounds inside a team's half (the half is 0.5 wide, full height), before the x origin.
            RectangleF withinHalf;

            switch (playersPerTeam)
            {
                case 1:
                    withinHalf = new RectangleF(0f, 0f, 0.5f, 1f);
                    break;

                case 2:
                    withinHalf = new RectangleF(0f, withinTeam * 0.5f, 0.5f, 0.5f);
                    break;

                case 3:
                    withinHalf = withinTeam == 0
                        ? new RectangleF(0.125f, 0f, 0.25f, 0.5f) // top, centred over the pair below
                        : new RectangleF((withinTeam - 1) * 0.25f, 0.5f, 0.25f, 0.5f); // bottom row
                    break;

                default: // 4
                    withinHalf = new RectangleF((withinTeam % 2) * 0.25f, (withinTeam / 2) * 0.5f, 0.25f, 0.5f);
                    break;
            }

            return new RectangleF(withinHalf.X + xOrigin, withinHalf.Y, withinHalf.Width, withinHalf.Height);
        }
    }
}
