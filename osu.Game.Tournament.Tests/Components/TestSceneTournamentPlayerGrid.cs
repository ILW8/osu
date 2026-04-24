// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Graphics;
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
    }
}
