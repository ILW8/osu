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

        private readonly Container?[] slotContainers = new Container?[MAX_SLOTS];
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
            if (slotContainers[slotIndex] != null)
                throw new System.InvalidOperationException($"Slot {slotIndex} is already occupied.");

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

            int visibleCount = 0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                if (slotContainers[i] != null && i < Capacity.Value)
                    visibleCount++;
            }

            (int cols, int rows) = dimensionsFor(visibleCount);
            if (cols == 0 || rows == 0)
                return;

            float cellWidth = DrawWidth / cols;
            float cellHeight = DrawHeight / rows;

            int cellIndex = 0;
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                var cell = slotContainers[i];
                if (cell == null)
                    continue;

                if (i >= Capacity.Value)
                {
                    cell.Alpha = 0;
                    continue;
                }

                int col = cellIndex % cols;
                int row = cellIndex / cols;

                cell.Alpha = 1;
                cell.Size = new osuTK.Vector2(cellWidth, cellHeight);
                cell.Position = new osuTK.Vector2(col * cellWidth, row * cellHeight);

                cellIndex++;
            }
        }

        private static (int cols, int rows) dimensionsFor(int visibleCount)
        {
            switch (visibleCount)
            {
                case 0:
                    return (0, 0);
                case 1:
                case 2:
                    return (2, 1);
                case 3:
                case 4:
                    return (2, 2);
                case 5:
                case 6:
                    return (3, 2);
                case 7:
                case 8:
                default:
                    return (4, 2);
            }
        }
    }
}
