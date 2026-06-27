// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class InitialSeekTest
    {
        [Test]
        public void NoOutliers_returnsMin()
        {
            Assert.That(TournamentSpectatorScreen.ComputeInitialSeekTime(new[] { 1000d, 1100d, 1200d }), Is.EqualTo(1000d));
        }

        [Test]
        public void LowOutlier_trimmedBeforeMin()
        {
            // mean ≈ 325; -5000 is more than 1000 below the mean so it's trimmed; min of the rest is 2000.
            Assert.That(TournamentSpectatorScreen.ComputeInitialSeekTime(new[] { -5000d, 2000d, 2100d, 2200d }), Is.EqualTo(2000d));
        }

        [Test]
        public void Empty_returnsZero()
        {
            Assert.That(TournamentSpectatorScreen.ComputeInitialSeekTime(System.Array.Empty<double>()), Is.EqualTo(0d));
        }
    }
}
