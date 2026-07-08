// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Tournament.Components;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class TournamentTeamScoreTest
    {
        [Test]
        public void SumsByTeam_team0Red_otherBlue_nullIgnored()
        {
            var (red, blue) = TournamentSpectatorScreen.SumTeamScores(new (int?, long)[]
            {
                (0, 100), // red
                (1, 200), // blue
                (0, 50), // red
                (1, 25), // blue
                (null, 9999), // no team state — ignored
            });

            Assert.That(red, Is.EqualTo(150));
            Assert.That(blue, Is.EqualTo(225));
        }

        [Test]
        public void EmptyInput_isZeroZero()
        {
            var (red, blue) = TournamentSpectatorScreen.SumTeamScores(Array.Empty<(int?, long)>());

            Assert.That(red, Is.EqualTo(0));
            Assert.That(blue, Is.EqualTo(0));
        }
    }
}
