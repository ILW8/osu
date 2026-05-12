// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.NonVisual
{
    /// <summary>
    /// Unit tests for <see cref="RoundBeatmapModFactory.ConstructMods"/>, the pure projection
    /// from a <see cref="RoundBeatmap"/>'s acronym string + <see cref="RoundBeatmap.ModParameters"/>
    /// to configured <see cref="osu.Game.Rulesets.Mods.Mod"/> instances.
    /// </summary>
    [TestFixture]
    public class RoundBeatmapModFactoryTest
    {
        [Test]
        public void EmptyModsStringReturnsEmpty()
        {
            var rb = new RoundBeatmap { Mods = string.Empty };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParsesMultipleTwoCharAcronyms()
        {
            var rb = new RoundBeatmap { Mods = "HDDT" };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());

            Assert.That(result.Select(m => m.Acronym), Is.EquivalentTo(new[] { "HD", "DT" }));
        }

        [Test]
        public void AppliesSettingsViaApiModRoundTrip()
        {
            var rb = new RoundBeatmap
            {
                Mods = "HDDT",
                ModParameters = new Dictionary<string, Dictionary<string, object>>
                {
                    ["DT"] = new Dictionary<string, object> { ["speed_change"] = 1.75 },
                },
            };

            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());

            var dt = result.OfType<OsuModDoubleTime>().Single();
            Assert.That(dt.SpeedChange.Value, Is.EqualTo(1.75).Within(0.001));
            Assert.That(((osu.Game.Rulesets.Mods.IMod)dt).HasNonDefaultSettings, Is.True);
        }

        [Test]
        public void IgnoresUnknownAcronyms()
        {
            var rb = new RoundBeatmap { Mods = "ZZ" };
            var result = RoundBeatmapModFactory.ConstructMods(rb, new OsuRuleset());
            Assert.That(result, Is.Empty);
        }
    }
}
