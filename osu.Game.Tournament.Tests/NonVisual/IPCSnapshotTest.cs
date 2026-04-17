// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
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
    }
}
