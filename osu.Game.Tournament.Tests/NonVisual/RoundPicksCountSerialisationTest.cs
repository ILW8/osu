// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class RoundPicksCountSerialisationTest
    {
        [Test]
        public void TestPicksCountSerialisesAsBestOfKey()
        {
            var round = new TournamentRound();
            round.PicksCount.Value = 11;

            string json = JsonConvert.SerializeObject(round);

            // Back-compat: the serialised key stays "BestOf" so existing bracket.json files keep working.
            Assert.That(json, Does.Contain("\"BestOf\""));
            Assert.That(json, Does.Not.Contain("\"PicksCount\""));
        }

        [Test]
        public void TestLegacyBestOfKeyDeserialisesIntoPicksCount()
        {
            const string legacy_json = "{\"Name\":\"Finals\",\"BestOf\":11}";

            var round = JsonConvert.DeserializeObject<TournamentRound>(legacy_json);

            Assert.That(round, Is.Not.Null);
            Assert.That(round!.PicksCount.Value, Is.EqualTo(11));
        }
    }
}
