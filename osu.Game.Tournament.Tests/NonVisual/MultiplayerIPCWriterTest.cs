// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Tests;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public partial class MultiplayerIPCWriterTest : TournamentHostTest
    {
        [Test]
        public void TestInitialWriteProducesDisconnectedJson()
        {
            using (HeadlessGameHost host = new CleanRunHeadlessGameHost())
            {
                try
                {
                    var tournament = new TestTournament(runOnLoadComplete: () => seedMultiplayerBracket(host));

                    LoadTournament(host, tournament);
                    tournament.BracketLoadTask.WaitSafely();

                    var storage = tournament.Dependencies.Get<Storage>();
                    string fullPath = storage.GetFullPath(
                        Path.Combine(MultiplayerIPCWriter.IPC_DIRECTORY, MultiplayerIPCWriter.IPC_FILENAME));

                    WaitForOrAssert(() => File.Exists(fullPath), $"expected {fullPath} to be created");

                    string json = File.ReadAllText(fullPath);
                    var parsed = JObject.Parse(json);

                    Assert.That(parsed["connected"]!.Value<bool>(), Is.False);
                    Assert.That(parsed["roomId"]!.Type, Is.EqualTo(JTokenType.Null));
                    Assert.That(parsed["users"]!.Type, Is.EqualTo(JTokenType.Array));
                    Assert.That(parsed["users"]!.HasValues, Is.False);
                }
                finally
                {
                    host.Exit();
                }
            }
        }

        [Test]
        public void TestFileUpdatesWhenScoresChange()
        {
            using (HeadlessGameHost host = new CleanRunHeadlessGameHost())
            {
                try
                {
                    var tournament = new TestTournament(runOnLoadComplete: () => seedMultiplayerBracket(host));
                    LoadTournament(host, tournament);
                    tournament.BracketLoadTask.WaitSafely();

                    var ipcInfo = tournament.Dependencies.Get<MultiplayerMatchIPCInfo>();
                    tournament.TestSchedule(() =>
                    {
                        // Flip the IPC source into the "connected" state so ComputeOutput
                        // projects live scores through instead of returning EmptyDisconnected.
                        ipcInfo.SetConnectedForTesting(true, roomId: 12345);
                        ipcInfo.Score1.Value = 42;
                        ipcInfo.Score2.Value = 17;
                    });

                    var storage = tournament.Dependencies.Get<Storage>();
                    string fullPath = storage.GetFullPath(
                        Path.Combine(MultiplayerIPCWriter.IPC_DIRECTORY, MultiplayerIPCWriter.IPC_FILENAME));

                    WaitForOrAssert(() =>
                    {
                        try
                        {
                            var parsed = JObject.Parse(File.ReadAllText(fullPath));
                            return parsed["scores"]!["team1"]!.Value<long>() == 42
                                && parsed["scores"]!["team2"]!.Value<long>() == 17;
                        }
                        catch { return false; }
                    }, "file did not reflect score change", 5000);
                }
                finally
                {
                    host.Exit();
                }
            }
        }

        /// <summary>
        /// Seeds <c>tournaments/default/bracket.json</c> with <c>UseMultiplayerSpectating = true</c>
        /// so the production branch in <see cref="TournamentGameBase"/> instantiates the writer.
        /// Must run from the tournament's <c>LoadComplete</c> (via <see cref="TestTournament"/>'s
        /// <c>runOnLoadComplete</c> hook), not before the host starts running —
        /// <see cref="GameHost.Storage"/> is only populated once the host has begun.
        /// </summary>
        private static void seedMultiplayerBracket(GameHost host)
        {
            var seedStorage = host.Storage.GetStorageForDirectory(Path.Combine("tournaments", "default"));
            using (var stream = seedStorage.CreateFileSafely("bracket.json"))
            using (var writer = new StreamWriter(stream))
                writer.Write("{ \"UseMultiplayerSpectating\": true }");
        }

        public partial class TestTournament : TournamentGameBase
        {
            private readonly Action? runOnLoadComplete;

            public new Task BracketLoadTask => base.BracketLoadTask;

            public TestTournament([InstantHandle] Action? runOnLoadComplete = null)
            {
                this.runOnLoadComplete = runOnLoadComplete;
            }

            protected override void LoadComplete()
            {
                runOnLoadComplete?.Invoke();
                base.LoadComplete();
            }

            /// <summary>
            /// Schedules an action on the update thread. Test-only helper because
            /// <see cref="osu.Framework.Graphics.Drawable.Scheduler"/> is protected.
            /// </summary>
            public void TestSchedule(Action action) => Schedule(action);
        }
    }
}
