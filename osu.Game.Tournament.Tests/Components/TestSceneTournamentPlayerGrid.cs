// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentPlayerGrid : TournamentTestScene
    {
        private TournamentPlayerGrid grid = null!;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Clear();
            Add(grid = new TournamentPlayerGrid
            {
                RelativeSizeAxes = Axes.Both,
            });
        });

        [Test]
        public void TestDefaultCapacityIsMinimum()
        {
            AddAssert("capacity defaults to MIN_SLOTS (2)",
                () => grid.Capacity.Value == TournamentPlayerGrid.MIN_SLOTS);
            AddAssert("MIN_SLOTS is 2", () => TournamentPlayerGrid.MIN_SLOTS == 2);
            AddAssert("MAX_SLOTS is 8", () => TournamentPlayerGrid.MAX_SLOTS == 8);
        }

        [Test]
        public void TestAddInsertsTileAtSlot()
        {
            Drawable? tile = null;
            AddStep("add tile at slot 0", () =>
            {
                tile = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Red };
                grid.Add(tile, 0);
            });
            AddAssert("tile is a descendant of grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == grid);
        }

        [Test]
        public void TestRemoveDisposesTileAtSlot()
        {
            Drawable? tile = null;
            AddStep("add tile at slot 3", () =>
            {
                tile = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Green };
                grid.Add(tile, 3);
            });
            AddAssert("tile is in grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == grid);
            AddStep("remove slot 3", () => grid.Remove(3));
            AddAssert("tile is no longer in grid", () => tile!.FindClosestParent<TournamentPlayerGrid>() == null);
        }

        [Test]
        public void TestClearRemovesAllTiles()
        {
            Drawable? a = null;
            Drawable? b = null;
            AddStep("add two tiles", () =>
            {
                a = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Red };
                b = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Blue };
                grid.Add(a, 0);
                grid.Add(b, 1);
            });
            AddStep("clear", () => grid.Clear());
            AddAssert("tile a gone", () => a!.FindClosestParent<TournamentPlayerGrid>() == null);
            AddAssert("tile b gone", () => b!.FindClosestParent<TournamentPlayerGrid>() == null);
        }

        [TestCase(2, 2, 1)]
        [TestCase(3, 2, 2)]
        [TestCase(4, 2, 2)]
        [TestCase(5, 3, 2)]
        [TestCase(6, 3, 2)]
        [TestCase(7, 4, 2)]
        [TestCase(8, 4, 2)]
        public void TestLayoutDimensionsForVisibleCount(int tileCount, int expectedCols, int expectedRows)
        {
            AddStep($"resize grid to 800x600", () =>
            {
                grid.RelativeSizeAxes = Axes.None;
                grid.Size = new osuTK.Vector2(800, 600);
            });
            AddStep($"set capacity to {tileCount}", () => grid.Capacity.Value = tileCount);
            AddStep($"add {tileCount} tiles", () =>
            {
                for (int i = 0; i < tileCount; i++)
                    grid.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Orange }, i);
            });
            AddUntilStep("tiles sized for layout",
                () =>
                {
                    float expectedTileWidth = 800f / expectedCols;
                    float expectedTileHeight = 600f / expectedRows;
                    foreach (var child in grid.ChildrenOfType<Box>())
                    {
                        if (System.Math.Abs(child.DrawWidth - expectedTileWidth) > 1f) return false;
                        if (System.Math.Abs(child.DrawHeight - expectedTileHeight) > 1f) return false;
                    }
                    return true;
                });
        }
    }
}
