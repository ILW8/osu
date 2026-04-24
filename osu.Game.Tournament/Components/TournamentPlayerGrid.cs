// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// A responsive grid of 2–8 tiles used by the tournament spectator overlay.
    /// Tile positions are addressed by a stable slot index; the visible subset
    /// is bounded by <see cref="Capacity"/>.
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

        private readonly Drawable?[] slots = new Drawable?[MAX_SLOTS];
        private readonly Container content;

        public TournamentPlayerGrid()
        {
            InternalChild = content = new Container { RelativeSizeAxes = Axes.Both };
        }

        public void Add(Drawable tile, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
                throw new System.ArgumentOutOfRangeException(nameof(slotIndex),
                    $"Slot index must be in [0, {MAX_SLOTS}).");
            if (slots[slotIndex] != null)
                throw new System.InvalidOperationException($"Slot {slotIndex} is already occupied.");

            slots[slotIndex] = tile;
            content.Add(tile);
        }

        public void Remove(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= MAX_SLOTS)
                return;
            var tile = slots[slotIndex];
            if (tile == null)
                return;

            slots[slotIndex] = null;
            content.Remove(tile, disposeImmediately: true);
        }

        public void Clear()
        {
            for (int i = 0; i < MAX_SLOTS; i++)
                slots[i] = null;
            content.Clear(disposeChildren: true);
        }
    }
}
