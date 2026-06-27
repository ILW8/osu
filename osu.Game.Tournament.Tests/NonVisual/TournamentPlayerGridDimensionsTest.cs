// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class TournamentPlayerGridDimensionsTest
    {
        [TestCase(1, 2, 1)]
        [TestCase(2, 2, 1)]
        [TestCase(3, 2, 2)]
        [TestCase(4, 2, 2)]
        [TestCase(5, 3, 2)]
        [TestCase(6, 3, 2)]
        [TestCase(7, 4, 2)]
        [TestCase(8, 4, 2)]
        public void DimensionsFor_matchesTable(int visible, int cols, int rows)
        {
            var dims = TournamentPlayerGrid.DimensionsFor(visible);
            Assert.That(dims.cols, Is.EqualTo(cols));
            Assert.That(dims.rows, Is.EqualTo(rows));
        }

        [Test]
        public void DimensionsFor_zero_isEmpty()
        {
            var dims = TournamentPlayerGrid.DimensionsFor(0);
            Assert.That(dims.cols, Is.EqualTo(0));
            Assert.That(dims.rows, Is.EqualTo(0));
        }
    }
}
