// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class LadderInfoSerialisationTest
    {
        [Test]
        public void TestDeserialise()
        {
            var ladder = createSampleLadder();
            string serialised = JsonConvert.SerializeObject(ladder);

            JsonConvert.DeserializeObject<LadderInfo>(serialised, new JsonPointConverter());
        }

        [Test]
        public void TestSerialise()
        {
            var ladder = createSampleLadder();
            JsonConvert.SerializeObject(ladder);
        }

        [Test]
        public void TestMatchSetRoundTrip()
        {
            // Regression for the BindableLong-no-parameterless-ctor bracket-load crash —
            // a bracket.json carrying a non-empty match.Sets[] would fail mid-deserialise
            // before MatchSet.Map{1,2,3}Id were switched to Bindable<long>.
            var ladder = createSampleLadder();
            var match = ladder.Matches[0];
            var set = new MatchSet();
            set.Map1Id.Value = 3508522;
            set.Map2Id.Value = 1234567;
            match.Sets.Add(set);

            string serialised = JsonConvert.SerializeObject(ladder);
            var roundTripped = JsonConvert.DeserializeObject<LadderInfo>(serialised, new JsonPointConverter());

            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.Matches[0].Sets, Has.Count.EqualTo(1));
            Assert.That(roundTripped.Matches[0].Sets[0].Map1Id.Value, Is.EqualTo(3508522));
            Assert.That(roundTripped.Matches[0].Sets[0].Map2Id.Value, Is.EqualTo(1234567));
            Assert.That(roundTripped.Matches[0].Sets[0].Map3Id.Value, Is.EqualTo(0));
        }

        private static LadderInfo createSampleLadder()
        {
            var match = TournamentTestScene.CreateSampleMatch();

            return new LadderInfo
            {
                PlayersPerTeam = { Value = 4 },
                Teams =
                {
                    match.Team1.Value!,
                    match.Team2.Value!,
                },
                Rounds =
                {
                    new TournamentRound
                    {
                        Beatmaps =
                        {
                            new RoundBeatmap { Beatmap = TournamentTestScene.CreateSampleBeatmap() },
                            new RoundBeatmap { Beatmap = TournamentTestScene.CreateSampleBeatmap() },
                        }
                    }
                },

                Matches =
                {
                    match,
                },
                Progressions =
                {
                    new TournamentProgression(1, 2),
                    new TournamentProgression(1, 3, true),
                }
            };
        }
    }
}
