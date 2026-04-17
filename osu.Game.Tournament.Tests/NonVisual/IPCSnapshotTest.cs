// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class IPCSnapshotTest
    {
        [Test]
        public void TestEmptyDisconnectedIsConsistent()
        {
            var a = IPCSnapshot.EmptyDisconnected;
            var b = IPCSnapshot.EmptyDisconnected;

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.Connected, Is.False);
            Assert.That(a.RoomId, Is.Null);
            Assert.That(a.BeatmapId, Is.Null);
            Assert.That(a.Team1Score, Is.EqualTo(0));
            Assert.That(a.Team2Score, Is.EqualTo(0));
            Assert.That(a.Users, Is.Empty);
        }

        [Test]
        public void TestSnapshotsWithSameDataAreEqual()
        {
            var users = ImmutableArray.Create(new IPCUserSnapshot(
                UserId: 42,
                TeamId: 1,
                Score: 1000,
                Combo: 10,
                Accuracy: 0.95,
                Hits: ImmutableDictionary<string, int>.Empty.Add("great", 5),
                GameplayTimeMs: 1234));

            var a = new IPCSnapshot(true, 1, 2, 1000, 0, users);
            var b = new IPCSnapshot(true, 1, 2, 1000, 0, users);

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void TestSnapshotsWithDifferentScoresAreNotEqual()
        {
            var a = new IPCSnapshot(true, 1, 2, 1000, 0, ImmutableArray<IPCUserSnapshot>.Empty);
            var b = new IPCSnapshot(true, 1, 2, 1001, 0, ImmutableArray<IPCUserSnapshot>.Empty);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void TestSerializeEmptyDisconnected()
        {
            string json = IPCSnapshot.SerializeToJson(IPCSnapshot.EmptyDisconnected);
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.That(parsed["connected"]!.Value<bool>(), Is.False);
            Assert.That(parsed["roomId"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null));
            Assert.That(parsed["beatmapId"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Null));
            Assert.That(parsed["scores"]!["team1"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(parsed["scores"]!["team2"]!.Value<long>(), Is.EqualTo(0));
            Assert.That(parsed["users"]!.Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Array));
            Assert.That(parsed["users"]!.HasValues, Is.False);
        }

        [Test]
        public void TestSerializePopulatedSnapshot()
        {
            var user = new IPCUserSnapshot(
                UserId: 9876,
                TeamId: 1,
                Score: 612345,
                Combo: 128,
                Accuracy: 0.9821,
                Hits: ImmutableDictionary<string, int>.Empty
                    .Add("great", 456)
                    .Add("ok", 7)
                    .Add("meh", 1)
                    .Add("miss", 2),
                GameplayTimeMs: 47320);

            var snap = new IPCSnapshot(true, 12345, 87654, 1234567, 1200000, ImmutableArray.Create(user));
            string json = IPCSnapshot.SerializeToJson(snap);
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.That(parsed["connected"]!.Value<bool>(), Is.True);
            Assert.That(parsed["roomId"]!.Value<long>(), Is.EqualTo(12345));
            Assert.That(parsed["beatmapId"]!.Value<int>(), Is.EqualTo(87654));
            Assert.That(parsed["scores"]!["team1"]!.Value<long>(), Is.EqualTo(1234567));
            Assert.That(parsed["scores"]!["team2"]!.Value<long>(), Is.EqualTo(1200000));

            var users = parsed["users"]!;
            Assert.That(users, Has.Count.EqualTo(1));
            var u0 = users[0]!;
            Assert.That(u0["userId"]!.Value<int>(), Is.EqualTo(9876));
            Assert.That(u0["teamId"]!.Value<int>(), Is.EqualTo(1));
            Assert.That(u0["score"]!.Value<long>(), Is.EqualTo(612345));
            Assert.That(u0["combo"]!.Value<int>(), Is.EqualTo(128));
            Assert.That(u0["accuracy"]!.Value<double>(), Is.EqualTo(0.9821).Within(1e-9));
            Assert.That(u0["hits"]!["great"]!.Value<int>(), Is.EqualTo(456));
            Assert.That(u0["hits"]!["miss"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(u0["gameplayTimeMs"]!.Value<double>(), Is.EqualTo(47320).Within(1e-9));
        }
    }
}
