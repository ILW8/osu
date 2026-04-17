# Tournament Multiplayer IPC Producer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce `ipc.json` under `<tournament>/ipc/` from the live multiplayer room so external overlays/scoreboards can consume match state by periodic re-reading, the same way they consumed stable client IPC files.

**Architecture:** Extend `MultiplayerMatchIPCInfo` to track per-user gameplay state alongside its existing score tracking. Add a new `MultiplayerIPCWriter : Component` that polls the IPC source at a configurable interval (50–500 ms, default 250), builds an immutable snapshot, and atomically writes JSON. Writer is instantiated only when multiplayer spectating is enabled.

**Tech Stack:** C# / .NET 8, osu!framework `Component` / `Bindable` / `Scheduler`, Newtonsoft.Json, NUnit, `InternalsVisibleTo` for test access.

**Spec reference:** `docs/superpowers/specs/2026-04-17-tournament-multiplayer-ipc-producer-design.md`

---

## File structure

**New files:**
- `osu.Game.Tournament/IPC/UserGameplayState.cs` — internal record struct holding per-user frame data.
- `osu.Game.Tournament/IPC/IPCSnapshot.cs` — `IPCSnapshot` + `IPCUserSnapshot` records + static `SerializeToJson` + static `ComputeOutput` state machine.
- `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs` — the `Component` that does the polling + I/O.
- `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs` — pure unit tests.
- `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs` — integration tests spinning up a `TestTournament`.

**Modified files:**
- `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs` — swap `userScores` dict for `userStates`, populate all five fields from each frame, expose `UserStates` accessor.
- `osu.Game.Tournament/Models/LadderInfo.cs` — add `IPCWriteIntervalMilliseconds` bindable.
- `osu.Game.Tournament/TournamentGameBase.cs` — instantiate `MultiplayerIPCWriter` in the multiplayer branch.
- `osu.Game.Tournament/Screens/Setup/SetupScreen.cs` — add interval slider + output-path label (visible in multiplayer mode only).

---

## Task 1: Extend `MultiplayerMatchIPCInfo` with full per-user gameplay state

**Goal:** Replace `userScores: Dictionary<int, long>` with `userStates: Dictionary<int, UserGameplayState>` so the writer can read every field it needs from a single source of truth — no second frame subscription.

**Files:**
- Create: `osu.Game.Tournament/IPC/UserGameplayState.cs`
- Modify: `osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs`

- [ ] **Step 1: Create `UserGameplayState.cs`**

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Per-user gameplay snapshot derived from a spectator <see cref="osu.Game.Online.Spectator.FrameDataBundle"/>.
    /// </summary>
    internal readonly record struct UserGameplayState(
        long Score,
        int Combo,
        double Accuracy,
        IReadOnlyDictionary<HitResult, int> Hits,
        double GameplayTimeMs)
    {
        public static UserGameplayState Empty { get; } = new UserGameplayState(
            Score: 0,
            Combo: 0,
            Accuracy: 0,
            Hits: new Dictionary<HitResult, int>(),
            GameplayTimeMs: 0);
    }
}
```

- [ ] **Step 2: Replace the `userScores` field in `MultiplayerMatchIPCInfo.cs`**

Find (around line 118):
```csharp
/// <summary>
/// Tracks the latest total score per user from spectator frame headers.
/// </summary>
private readonly Dictionary<int, long> userScores = new Dictionary<int, long>();
```

Replace with:
```csharp
/// <summary>
/// Tracks the latest gameplay snapshot per user from spectator frame bundles.
/// Exposed via <see cref="UserStates"/> for the IPC writer.
/// </summary>
private readonly Dictionary<int, UserGameplayState> userStates = new Dictionary<int, UserGameplayState>();

/// <summary>
/// Read-only view of the latest per-user gameplay state. Mutated on the update thread
/// from spectator frame bundles; consumers should also read from the update thread.
/// </summary>
internal IReadOnlyDictionary<int, UserGameplayState> UserStates => userStates;
```

- [ ] **Step 3: Update `startWatchingUser` and `stopWatchingUser`**

Find:
```csharp
spectatorClient.WatchUser(userId);
userScores[userId] = 0;
```
Replace with:
```csharp
spectatorClient.WatchUser(userId);
userStates[userId] = UserGameplayState.Empty;
```

Find:
```csharp
spectatorClient.StopWatchingUser(userId);
userScores.Remove(userId);
```
Replace with:
```csharp
spectatorClient.StopWatchingUser(userId);
userStates.Remove(userId);
```

- [ ] **Step 4: Extend `onNewFrames` to capture all fields**

Find:
```csharp
private void onNewFrames(int userId, FrameDataBundle bundle)
{
    if (!watchedUsers.Contains(userId))
        return;

    Schedule(() =>
    {
        userScores[userId] = bundle.Header.TotalScore;
        updateTeamScores();
    });
}
```

Replace with:
```csharp
private void onNewFrames(int userId, FrameDataBundle bundle)
{
    if (!watchedUsers.Contains(userId))
        return;

    Schedule(() =>
    {
        var header = bundle.Header;
        double gameplayTime = bundle.Frames.Count > 0 ? bundle.Frames[^1].Time : 0;

        userStates[userId] = new UserGameplayState(
            Score: header.TotalScore,
            Combo: header.Combo,
            Accuracy: header.Accuracy,
            Hits: new Dictionary<HitResult, int>(header.Statistics),
            GameplayTimeMs: gameplayTime);

        updateTeamScores();
    });
}
```

(The `Hits` dict is copied so later mutations on `header.Statistics` don't leak into stored state.)

- [ ] **Step 5: Update `onLoadRequested` and `Disconnect` to reset the new dict**

In `onLoadRequested`, find:
```csharp
// Reset scores for the new round.
foreach (int userId in userScores.Keys.ToArray())
    userScores[userId] = 0;
```
Replace with:
```csharp
// Reset per-user state for the new round. Users are re-populated on next frame.
foreach (int userId in userStates.Keys.ToArray())
    userStates[userId] = UserGameplayState.Empty;
```

In `Disconnect` (inside the `Schedule(() => { ... })` block), find:
```csharp
lastBeatmapId = 0;
userScores.Clear();
```
Replace with:
```csharp
lastBeatmapId = 0;
userStates.Clear();
```

- [ ] **Step 6: Update `updateTeamScores` to read `.Score`**

Find:
```csharp
if (!userScores.TryGetValue(user.UserID, out long score))
    continue;

if (teamState.TeamID == 0)
    team0Score += score;
else
    team1Score += score;
```

Replace with:
```csharp
if (!userStates.TryGetValue(user.UserID, out var state))
    continue;

if (teamState.TeamID == 0)
    team0Score += state.Score;
else
    team1Score += state.Score;
```

- [ ] **Step 7: Add `using osu.Game.Rulesets.Scoring;` to the top of `MultiplayerMatchIPCInfo.cs`**

The file already imports various `osu.Game` namespaces. Add the `Rulesets.Scoring` import so `HitResult` is in scope for the new `Dictionary<HitResult, int>` copy in `onNewFrames`.

- [ ] **Step 8: Build**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add osu.Game.Tournament/IPC/UserGameplayState.cs osu.Game.Tournament/IPC/MultiplayerMatchIPCInfo.cs
git commit -m "$(cat <<'EOF'
track full per-user gameplay state in MultiplayerMatchIPCInfo

Replaces the private userScores dict with userStates holding score,
combo, accuracy, hits, and gameplay time. Exposes UserStates for the
upcoming IPC writer to consume without a duplicate frame subscription.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add `IPCWriteIntervalMilliseconds` to `LadderInfo`

**Goal:** Persist a configurable write interval so the operator can tune it from the Setup screen.

**Files:**
- Modify: `osu.Game.Tournament/Models/LadderInfo.cs`

- [ ] **Step 1: Add the bindable**

Add after the `UseMultiplayerSpectating` bindable (around line 52) in `LadderInfo.cs`:

```csharp
/// <summary>
/// Interval in milliseconds between IPC file writes when multiplayer spectating is active.
/// Defaults to 250 ms to match the stable client's historical IPC polling cadence.
/// </summary>
public Bindable<int> IPCWriteIntervalMilliseconds = new BindableInt(250)
{
    MinValue = 50,
    MaxValue = 500,
};
```

- [ ] **Step 2: Build**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add osu.Game.Tournament/Models/LadderInfo.cs
git commit -m "add IPCWriteIntervalMilliseconds to LadderInfo (default 250 ms, range 50-500)"
```

---

## Task 3: Add `IPCSnapshot` + `IPCUserSnapshot` record types with equality test

**Goal:** Define the immutable snapshot types whose structural equality drives the writer's dirty-check.

**Files:**
- Create: `osu.Game.Tournament/IPC/IPCSnapshot.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`

- [ ] **Step 1: Write failing test for record equality**

Create `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`:

```csharp
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
```

- [ ] **Step 2: Run test — expect fail (types don't exist)**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest"`
Expected: build error — `IPCSnapshot` / `IPCUserSnapshot` not found.

- [ ] **Step 3: Create `IPCSnapshot.cs`**

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Immutable snapshot of the multiplayer room state at a single point in time.
    /// Drives structural-equality dirty checks and JSON serialization in <see cref="MultiplayerIPCWriter"/>.
    /// </summary>
    internal readonly record struct IPCSnapshot(
        bool Connected,
        long? RoomId,
        int? BeatmapId,
        long Team1Score,
        long Team2Score,
        ImmutableArray<IPCUserSnapshot> Users)
    {
        public static IPCSnapshot EmptyDisconnected { get; } = new IPCSnapshot(
            Connected: false,
            RoomId: null,
            BeatmapId: null,
            Team1Score: 0,
            Team2Score: 0,
            Users: ImmutableArray<IPCUserSnapshot>.Empty);
    }

    /// <summary>
    /// Per-user gameplay data included in an <see cref="IPCSnapshot"/>.
    /// </summary>
    /// <param name="TeamId">1-indexed team number (internal <c>TeamVersusUserState.TeamID</c> + 1).</param>
    /// <param name="Hits">Lowercase <c>HitResult</c> enum name → count. Keys vary by ruleset.</param>
    internal readonly record struct IPCUserSnapshot(
        int UserId,
        int TeamId,
        long Score,
        int Combo,
        double Accuracy,
        ImmutableDictionary<string, int> Hits,
        double GameplayTimeMs);
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/IPCSnapshot.cs osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs
git commit -m "add IPCSnapshot/IPCUserSnapshot records for tournament IPC output"
```

---

## Task 4: Add `IPCSnapshot.SerializeToJson` with schema tests

**Goal:** Serialize snapshots into the JSON shape from spec §4. Using `Newtonsoft.Json.Linq` directly (rather than attribute-based serialization) gives exact control over key names, null emission, and ordering without decorating the record types.

**Files:**
- Modify: `osu.Game.Tournament/IPC/IPCSnapshot.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`

- [ ] **Step 1: Add failing schema tests**

Append inside the `IPCSnapshotTest` class:

```csharp
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
```

- [ ] **Step 2: Run tests — expect fail**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest.TestSerialize"`
Expected: build error — `SerializeToJson` not defined.

- [ ] **Step 3: Implement `SerializeToJson`**

Replace the contents of `osu.Game.Tournament/IPC/IPCSnapshot.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Immutable snapshot of the multiplayer room state at a single point in time.
    /// Drives structural-equality dirty checks and JSON serialization in <see cref="MultiplayerIPCWriter"/>.
    /// </summary>
    internal readonly record struct IPCSnapshot(
        bool Connected,
        long? RoomId,
        int? BeatmapId,
        long Team1Score,
        long Team2Score,
        ImmutableArray<IPCUserSnapshot> Users)
    {
        public static IPCSnapshot EmptyDisconnected { get; } = new IPCSnapshot(
            Connected: false,
            RoomId: null,
            BeatmapId: null,
            Team1Score: 0,
            Team2Score: 0,
            Users: ImmutableArray<IPCUserSnapshot>.Empty);

        /// <summary>
        /// Serializes a snapshot to the JSON schema documented in the design spec.
        /// </summary>
        public static string SerializeToJson(IPCSnapshot snap)
        {
            var users = new JArray();
            foreach (var u in snap.Users)
            {
                var hits = new JObject();
                foreach (var (key, count) in u.Hits)
                    hits[key] = count;

                users.Add(new JObject
                {
                    ["userId"] = u.UserId,
                    ["teamId"] = u.TeamId,
                    ["score"] = u.Score,
                    ["combo"] = u.Combo,
                    ["accuracy"] = u.Accuracy,
                    ["hits"] = hits,
                    ["gameplayTimeMs"] = u.GameplayTimeMs,
                });
            }

            var root = new JObject
            {
                ["connected"] = snap.Connected,
                ["roomId"] = snap.RoomId.HasValue ? new JValue(snap.RoomId.Value) : JValue.CreateNull(),
                ["beatmapId"] = snap.BeatmapId.HasValue ? new JValue(snap.BeatmapId.Value) : JValue.CreateNull(),
                ["scores"] = new JObject
                {
                    ["team1"] = snap.Team1Score,
                    ["team2"] = snap.Team2Score,
                },
                ["users"] = users,
            };

            return root.ToString(Formatting.None);
        }
    }

    /// <summary>
    /// Per-user gameplay data included in an <see cref="IPCSnapshot"/>.
    /// </summary>
    /// <param name="TeamId">1-indexed team number (internal <c>TeamVersusUserState.TeamID</c> + 1).</param>
    /// <param name="Hits">Lowercase <c>HitResult</c> enum name → count. Keys vary by ruleset.</param>
    internal readonly record struct IPCUserSnapshot(
        int UserId,
        int TeamId,
        long Score,
        int Combo,
        double Accuracy,
        ImmutableDictionary<string, int> Hits,
        double GameplayTimeMs);
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest"`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/IPCSnapshot.cs osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs
git commit -m "add IPCSnapshot.SerializeToJson for tournament ipc.json output"
```

---

## Task 5: Add `ComputeOutput` state machine (disconnect preservation)

**Goal:** Pure static method encapsulating the "keep last frame on disconnect, clear on new connect" rule. Testable without any Component machinery.

**Behavior matrix** (from spec §6):

| `live.Connected` | `wasConnected` going in | Output |
| --- | --- | --- |
| true | any | `live`; update `lastConnectedSnapshot = live`; clear first if was false (new session) |
| false | any | `lastConnectedSnapshot with Connected = false` if set, else `EmptyDisconnected` |

**Files:**
- Modify: `osu.Game.Tournament/IPC/IPCSnapshot.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs`

- [ ] **Step 1: Write failing tests for all four transitions**

Append to the `IPCSnapshotTest` class:

```csharp
[Test]
public void TestComputeOutput_NeverConnected_ReturnsEmpty()
{
    IPCSnapshot? last = null;
    bool wasConnected = false;

    var output = IPCSnapshot.ComputeOutput(
        IPCSnapshot.EmptyDisconnected,
        ref last,
        ref wasConnected);

    Assert.That(output, Is.EqualTo(IPCSnapshot.EmptyDisconnected));
    Assert.That(last, Is.Null);
    Assert.That(wasConnected, Is.False);
}

[Test]
public void TestComputeOutput_Connected_ReturnsLiveAndRemembers()
{
    IPCSnapshot? last = null;
    bool wasConnected = false;

    var live = new IPCSnapshot(true, 77, 99, 100, 200, ImmutableArray<IPCUserSnapshot>.Empty);
    var output = IPCSnapshot.ComputeOutput(live, ref last, ref wasConnected);

    Assert.That(output, Is.EqualTo(live));
    Assert.That(last, Is.EqualTo(live));
    Assert.That(wasConnected, Is.True);
}

[Test]
public void TestComputeOutput_DisconnectAfterSession_PreservesLastFrame()
{
    IPCSnapshot? last = null;
    bool wasConnected = false;

    var live = new IPCSnapshot(true, 77, 99, 100, 200, ImmutableArray<IPCUserSnapshot>.Empty);
    IPCSnapshot.ComputeOutput(live, ref last, ref wasConnected);

    var output = IPCSnapshot.ComputeOutput(IPCSnapshot.EmptyDisconnected, ref last, ref wasConnected);

    Assert.That(output.Connected, Is.False);
    Assert.That(output.RoomId, Is.EqualTo(77));
    Assert.That(output.BeatmapId, Is.EqualTo(99));
    Assert.That(output.Team1Score, Is.EqualTo(100));
    Assert.That(output.Team2Score, Is.EqualTo(200));
    Assert.That(wasConnected, Is.False);
}

[Test]
public void TestComputeOutput_NewConnection_ClearsOldSession()
{
    IPCSnapshot? last = null;
    bool wasConnected = false;

    var sessionA = new IPCSnapshot(true, 77, 99, 100, 200, ImmutableArray<IPCUserSnapshot>.Empty);
    IPCSnapshot.ComputeOutput(sessionA, ref last, ref wasConnected);
    IPCSnapshot.ComputeOutput(IPCSnapshot.EmptyDisconnected, ref last, ref wasConnected);
    // last now holds sessionA; wasConnected is false.

    var sessionB = new IPCSnapshot(true, 88, 111, 0, 0, ImmutableArray<IPCUserSnapshot>.Empty);
    var output = IPCSnapshot.ComputeOutput(sessionB, ref last, ref wasConnected);

    Assert.That(output, Is.EqualTo(sessionB));
    Assert.That(last, Is.EqualTo(sessionB));
    Assert.That(output.RoomId, Is.EqualTo(88));
}
```

- [ ] **Step 2: Run tests — expect fail**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest.TestComputeOutput"`
Expected: build error — `IPCSnapshot.ComputeOutput` not defined.

- [ ] **Step 3: Implement `ComputeOutput`**

In `osu.Game.Tournament/IPC/IPCSnapshot.cs`, inside the `IPCSnapshot` record, after `SerializeToJson`, add:

```csharp
/// <summary>
/// Given a live snapshot plus the writer's persisted state (last-connected snapshot
/// and previous-tick connection flag), returns the snapshot to actually serialize.
/// Implements the disconnect-preservation rule from the design spec:
/// on disconnect, reuse the last connected snapshot with <c>Connected = false</c>;
/// on reconnect, drop the old session's last-connected snapshot and take the new one.
/// </summary>
public static IPCSnapshot ComputeOutput(
    IPCSnapshot live,
    ref IPCSnapshot? lastConnectedSnapshot,
    ref bool wasConnected)
{
    if (live.Connected)
    {
        // New connection (was false, now true): drop any previous session's memory.
        if (!wasConnected)
            lastConnectedSnapshot = null;

        lastConnectedSnapshot = live;
        wasConnected = true;
        return live;
    }

    wasConnected = false;

    if (lastConnectedSnapshot is { } remembered)
        return remembered with { Connected = false };

    return EmptyDisconnected;
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~IPCSnapshotTest"`
Expected: 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/IPCSnapshot.cs osu.Game.Tournament.Tests/NonVisual/IPCSnapshotTest.cs
git commit -m "add IPCSnapshot.ComputeOutput state machine for disconnect preservation"
```

---

## Task 6: Create `MultiplayerIPCWriter` with initial write + wire into `TournamentGameBase`

**Goal:** Wire the writer into the production multiplayer branch. On component load it creates `ipc/`, writes the `EmptyDisconnected` snapshot atomically, and is ready to tick (tick is added in Task 7). Integration test verifies the file appears with the right contents when the tournament loads in multiplayer mode.

**Files:**
- Create: `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`
- Modify: `osu.Game.Tournament/TournamentGameBase.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs`

- [ ] **Step 1: Write failing integration test**

Create `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Tests;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

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
                    seedMultiplayerBracket(host);

                    var tournament = new TestTournament();
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

        /// <summary>
        /// Seeds <c>tournaments/default/bracket.json</c> with <c>UseMultiplayerSpectating = true</c>
        /// so the production branch in <see cref="TournamentGameBase"/> instantiates the writer.
        /// Must run before <see cref="LoadTournament"/>.
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
            public new Task BracketLoadTask => base.BracketLoadTask;

            /// <summary>
            /// Schedules an action on the update thread. Test-only helper because
            /// <see cref="osu.Framework.Graphics.Drawable.Scheduler"/> is protected.
            /// </summary>
            public void TestSchedule(System.Action action) => Schedule(action);
        }
    }
}
```

- [ ] **Step 2: Run test — expect fail (type doesn't exist)**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest"`
Expected: build error — `MultiplayerIPCWriter` not defined.

- [ ] **Step 3: Create the writer**

Create `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Writes live multiplayer room state to <c>ipc.json</c> under the tournament
    /// storage so external overlays and scoreboards can consume it by polling.
    /// Instantiated only when multiplayer spectating is active.
    /// </summary>
    internal partial class MultiplayerIPCWriter : Component
    {
        public const string IPC_DIRECTORY = "ipc";
        public const string IPC_FILENAME = "ipc.json";
        private const string ipc_tmp_filename = "ipc.json.tmp";

        [Resolved]
        private Storage storage { get; set; } = null!;

        private Storage ipcStorage = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);
            writeAtomically(IPCSnapshot.SerializeToJson(IPCSnapshot.EmptyDisconnected));
        }

        /// <summary>
        /// Serialize-to-temp + atomic rename so consumers never see a partial file.
        /// </summary>
        private void writeAtomically(string json)
        {
            string tmpFullPath = ipcStorage.GetFullPath(ipc_tmp_filename);
            string finalFullPath = ipcStorage.GetFullPath(IPC_FILENAME);

            try
            {
                File.WriteAllText(tmpFullPath, json);
                File.Move(tmpFullPath, finalFullPath, overwrite: true);
            }
            catch (IOException e)
            {
                Logger.Log($"[MultiplayerIPCWriter] Failed to write {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
        }
    }
}
```

- [ ] **Step 4: Wire the writer into `TournamentGameBase`**

Find (around line 209–222 of `TournamentGameBase.cs`):

```csharp
if (ladder.UseMultiplayerSpectating.Value)
{
    var multiplayerIpc = new MultiplayerMatchIPCInfo();
    dependencies.CacheAs<MatchIPCInfo>(multiplayerIpc);
    dependencies.CacheAs(multiplayerIpc);
    ipc = multiplayerIpc;
}
else
{
    ipc = new FileBasedIPC();
    dependencies.CacheAs<MatchIPCInfo>(ipc);
}

Add(ipc);
```

Replace with:

```csharp
if (ladder.UseMultiplayerSpectating.Value)
{
    var multiplayerIpc = new MultiplayerMatchIPCInfo();
    dependencies.CacheAs<MatchIPCInfo>(multiplayerIpc);
    dependencies.CacheAs(multiplayerIpc);
    ipc = multiplayerIpc;
}
else
{
    ipc = new FileBasedIPC();
    dependencies.CacheAs<MatchIPCInfo>(ipc);
}

Add(ipc);

if (ipc is MultiplayerMatchIPCInfo)
    Add(new MultiplayerIPCWriter());
```

- [ ] **Step 5: Run test — expect pass**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest.TestInitial"`
Expected: test passes. File `tournaments/default/ipc/ipc.json` exists with `connected: false`, null ids, and empty users array.

- [ ] **Step 6: Commit**

```bash
git add osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs osu.Game.Tournament/TournamentGameBase.cs osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs
git commit -m "add MultiplayerIPCWriter with initial disconnected write + wire into TournamentGameBase"
```

---

## Task 7: Add polling tick, snapshot build, and dirty skip

**Goal:** Every `interval` ms, build an `IPCSnapshot` from `MultiplayerMatchIPCInfo` + `MultiplayerClient.Room`, apply `ComputeOutput`, and write only if the result differs from the last write.

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs`

- [ ] **Step 1: Write failing integration test — score changes propagate to the file**

Append to `MultiplayerIPCWriterTest` class:

```csharp
[Test]
public void TestFileUpdatesWhenScoresChange()
{
    using (HeadlessGameHost host = new CleanRunHeadlessGameHost())
    {
        try
        {
            seedMultiplayerBracket(host);

            var tournament = new TestTournament();
            LoadTournament(host, tournament);
            tournament.BracketLoadTask.WaitSafely();

            var ipcInfo = tournament.Dependencies.Get<MultiplayerMatchIPCInfo>();
            tournament.TestSchedule(() =>
            {
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
```

- [ ] **Step 2: Run — expect fail (no tick yet, file stays at 0/0)**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest.TestFileUpdates"`
Expected: timeout — file is still `{ team1: 0, team2: 0 }`.

- [ ] **Step 3: Extend the writer with polling + snapshot build + dirty skip**

Replace `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.IPC
{
    /// <summary>
    /// Writes live multiplayer room state to <c>ipc.json</c> under the tournament
    /// storage so external overlays and scoreboards can consume it by polling.
    /// Instantiated only when multiplayer spectating is active.
    /// </summary>
    internal partial class MultiplayerIPCWriter : Component
    {
        public const string IPC_DIRECTORY = "ipc";
        public const string IPC_FILENAME = "ipc.json";
        private const string ipc_tmp_filename = "ipc.json.tmp";

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private MultiplayerMatchIPCInfo ipcInfo { get; set; } = null!;

        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private Storage ipcStorage = null!;
        private ScheduledDelegate? tickDelegate;

        // Writer-owned state driving the disconnect-preservation rule.
        private IPCSnapshot? lastConnectedSnapshot;
        private bool wasConnected;

        // Last successfully-written snapshot; used for the dirty-check.
        private IPCSnapshot? lastWrittenSnapshot;

        [BackgroundDependencyLoader]
        private void load()
        {
            ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);

            var initial = IPCSnapshot.EmptyDisconnected;
            writeAtomically(IPCSnapshot.SerializeToJson(initial));
            lastWrittenSnapshot = initial;

            tickDelegate = Scheduler.AddDelayed(tick, ladder.IPCWriteIntervalMilliseconds.Value, true);
        }

        private void tick()
        {
            var live = buildLiveSnapshot();
            var output = IPCSnapshot.ComputeOutput(live, ref lastConnectedSnapshot, ref wasConnected);

            if (lastWrittenSnapshot.HasValue && lastWrittenSnapshot.Value.Equals(output))
                return;

            writeAtomically(IPCSnapshot.SerializeToJson(output));
            lastWrittenSnapshot = output;
        }

        /// <summary>
        /// Project live <see cref="MultiplayerMatchIPCInfo"/> + <see cref="MultiplayerClient.Room"/>
        /// state into an <see cref="IPCSnapshot"/>. Must run on the update thread.
        /// </summary>
        private IPCSnapshot buildLiveSnapshot()
        {
            bool connected = ipcInfo.IsConnected.Value;
            long? roomId = ipcInfo.ConnectedRoomId.Value;
            int? beatmapId = ipcInfo.Beatmap.Value?.OnlineID;
            long score1 = ipcInfo.Score1.Value;
            long score2 = ipcInfo.Score2.Value;

            var users = ImmutableArray.CreateBuilder<IPCUserSnapshot>();

            if (connected && multiplayerClient.Room is { } room)
            {
                foreach (var roomUser in room.Users)
                {
                    if (roomUser.MatchState is not TeamVersusUserState teamState)
                        continue;

                    if (!ipcInfo.UserStates.TryGetValue(roomUser.UserID, out var state))
                        continue;

                    var hitsBuilder = ImmutableDictionary.CreateBuilder<string, int>();
                    foreach (var (result, count) in state.Hits)
                        hitsBuilder[result.ToString().ToLowerInvariant()] = count;

                    users.Add(new IPCUserSnapshot(
                        UserId: roomUser.UserID,
                        TeamId: teamState.TeamID + 1, // 1-indexed per schema
                        Score: state.Score,
                        Combo: state.Combo,
                        Accuracy: state.Accuracy,
                        Hits: hitsBuilder.ToImmutable(),
                        GameplayTimeMs: state.GameplayTimeMs));
                }
            }

            return new IPCSnapshot(
                Connected: connected,
                RoomId: roomId,
                BeatmapId: beatmapId,
                Team1Score: score1,
                Team2Score: score2,
                Users: users.ToImmutable());
        }

        private void writeAtomically(string json)
        {
            string tmpFullPath = ipcStorage.GetFullPath(ipc_tmp_filename);
            string finalFullPath = ipcStorage.GetFullPath(IPC_FILENAME);

            try
            {
                File.WriteAllText(tmpFullPath, json);
                File.Move(tmpFullPath, finalFullPath, overwrite: true);
            }
            catch (IOException e)
            {
                Logger.Log($"[MultiplayerIPCWriter] Failed to write {IPC_FILENAME}: {e.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            tickDelegate?.Cancel();
        }
    }
}
```

- [ ] **Step 4: Run both writer tests**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest"`
Expected: both tests pass — initial file appears, score changes are reflected within ~250 ms.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs
git commit -m "add polling tick + snapshot build + dirty-check to MultiplayerIPCWriter"
```

---

## Task 8: Reschedule tick when interval bindable changes

**Goal:** Operator drags the slider → cadence changes without restart.

**Files:**
- Modify: `osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs`

- [ ] **Step 1: Write failing test**

This is a smoke test: change the interval at runtime (operator drags the slider) and verify the writer keeps producing valid writes without crashing. It doesn't assert on timing (which would be flaky), only on correctness-after-change.

Append to `MultiplayerIPCWriterTest` class:

```csharp
[Test]
public void TestIntervalChangeDoesNotBreakWrites()
{
    using (HeadlessGameHost host = new CleanRunHeadlessGameHost())
    {
        try
        {
            seedMultiplayerBracket(host);

            var tournament = new TestTournament();
            LoadTournament(host, tournament);
            tournament.BracketLoadTask.WaitSafely();

            var ladder = tournament.Dependencies.Get<LadderInfo>();
            var ipcInfo = tournament.Dependencies.Get<MultiplayerMatchIPCInfo>();

            // Toggle the interval a few times, then change a tracked value.
            tournament.TestSchedule(() =>
            {
                ladder.IPCWriteIntervalMilliseconds.Value = 500;
                ladder.IPCWriteIntervalMilliseconds.Value = 50;
                ipcInfo.Score1.Value = 999;
            });

            var storage = tournament.Dependencies.Get<Storage>();
            string fullPath = storage.GetFullPath(
                Path.Combine(MultiplayerIPCWriter.IPC_DIRECTORY, MultiplayerIPCWriter.IPC_FILENAME));

            WaitForOrAssert(() =>
            {
                try
                {
                    var parsed = JObject.Parse(File.ReadAllText(fullPath));
                    return parsed["scores"]!["team1"]!.Value<long>() == 999;
                }
                catch { return false; }
            }, "file did not reflect score change after interval toggling", 3000);
        }
        finally
        {
            host.Exit();
        }
    }
}
```

- [ ] **Step 2: Run — expect fail if the writer doesn't subscribe to the bindable**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest.TestIntervalChange"`

Expected result depends on current state: if the Task 7 implementation schedules ticks unconditionally in `load()` (not via the bindable), this test will likely pass — interval changes are just ignored and the original 250 ms tick still fires. That's exactly the behavior we need to eliminate. To make the test actually fail pre-fix, first remove the `Scheduler.AddDelayed(...)` line from Task 7's `load()` entirely, confirm the test times out, then re-add it via the bindable-bound path below.

(If you prefer to skip the "fail first" verification for this task, that's fine — the behavior change is small and low-risk. Just ensure the post-fix test passes.)

- [ ] **Step 3: Subscribe to the interval bindable in `load()`**

In `MultiplayerIPCWriter.cs`, replace the `load()` method with:

```csharp
[BackgroundDependencyLoader]
private void load()
{
    ipcStorage = storage.GetStorageForDirectory(IPC_DIRECTORY);

    var initial = IPCSnapshot.EmptyDisconnected;
    writeAtomically(IPCSnapshot.SerializeToJson(initial));
    lastWrittenSnapshot = initial;

    ladder.IPCWriteIntervalMilliseconds.BindValueChanged(
        e => rescheduleTicks(e.NewValue),
        runOnceImmediately: true);
}

private void rescheduleTicks(int intervalMs)
{
    tickDelegate?.Cancel();
    tickDelegate = Scheduler.AddDelayed(tick, intervalMs, true);
}
```

Also remove the now-redundant `Scheduler.AddDelayed(tick, ...)` call that used to be at the bottom of `load()` (it's now inside `rescheduleTicks`, called immediately on bind).

- [ ] **Step 4: Run all writer tests**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~MultiplayerIPCWriterTest"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/IPC/MultiplayerIPCWriter.cs osu.Game.Tournament.Tests/NonVisual/MultiplayerIPCWriterTest.cs
git commit -m "reschedule MultiplayerIPCWriter ticks when LadderInfo interval changes"
```

---

## Task 9: Add interval slider + output-path label to Setup screen

**Goal:** Operator-facing UI for the interval and a visible pointer to the output file. Both visible only when `UseMultiplayerSpectating` is on.

**Files:**
- Modify: `osu.Game.Tournament/Screens/Setup/SetupScreen.cs`

- [ ] **Step 1: Add a `Storage` dependency to `SetupScreen`**

Near the other `[Resolved]` declarations at the top of the class, add:

```csharp
[Resolved]
private Storage storage { get; set; } = null!;
```

And at the top of the file, add the needed usings (append to the existing `using` block):

```csharp
using System.IO;
using osu.Framework.Platform;
```

- [ ] **Step 2: Add the slider and path label to `fillFlow.Children`**

In `reload()`, find the `Display team seeds` `LabelledSwitchButton` entry. Right after it (before `Mute UI sounds`), insert:

```csharp
new SettingsSlider<int>
{
    LabelText = "Multiplayer IPC write interval (ms)",
    Current = LadderInfo.IPCWriteIntervalMilliseconds,
    KeyboardStep = 10,
    Alpha = LadderInfo.UseMultiplayerSpectating.Value ? 1 : 0,
},
new ActionableInfo
{
    Label = "Multiplayer IPC output path",
    ButtonText = "Open folder",
    Action = () => storage.GetStorageForDirectory(MultiplayerIPCWriter.IPC_DIRECTORY).PresentExternally(),
    Value = storage.GetFullPath(
        Path.Combine(MultiplayerIPCWriter.IPC_DIRECTORY, MultiplayerIPCWriter.IPC_FILENAME)),
    Description = "External overlays and scoreboards can poll this file for live room state.",
    Alpha = LadderInfo.UseMultiplayerSpectating.Value ? 1 : 0,
},
```

- [ ] **Step 3: Build**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`
Expected: 0 errors.

- [ ] **Step 4: Manual verification**

Launch the tournament overlay. On the Setup screen:
- Toggle `Use multiplayer spectating` off → the new slider and path label should have `Alpha = 0` (hidden).
- Toggle it on → save & restart → both controls appear. The slider shows `250`; the label shows a path ending in `…\tournaments\<tourney>\ipc\ipc.json`.

(If the live `Alpha` toggle needs to react without a restart too, that's a follow-up — the `reload()` pattern in the rest of the screen already handles visibility transitions on ladder changes, but this slider/label pair intentionally doesn't require mid-session reactivity because the writer itself is gated on the pre-restart value.)

- [ ] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Screens/Setup/SetupScreen.cs
git commit -m "add multiplayer IPC interval slider and output path label to Setup screen"
```

---

## Self-review

**Spec coverage:**

| Spec section | Covered by |
| --- | --- |
| §2 Scope (producer only in multiplayer mode) | Task 6 wiring |
| §3 Output file path + atomic writes | Task 6 (initial), Task 7 (tick) `writeAtomically` |
| §4 JSON schema | Task 4 `SerializeToJson` + tests |
| §5.1 `MultiplayerMatchIPCInfo` changes | Task 1 |
| §5.2 Writer component lifecycle (load, ticks, dispose) | Tasks 6–8 |
| §5.3 Snapshot record types | Tasks 3–4 |
| §5.4 Dirty check | Task 7 |
| §6 Disconnect / reconnect semantics | Task 5 pure state machine + Task 7 tick calls `ComputeOutput` |
| §7 Configuration (`IPCWriteIntervalMilliseconds`) | Task 2 (bindable); Task 9 (UI) |
| §8 Wiring into `TournamentGameBase` | Task 6 |
| §9 Testing (initial write, schema, dirty skip, throttle, interval change, disconnect preserves, new connection clears, atomic write) | Tasks 3–8 collectively |

**Known gaps / acceptable trade-offs:**

- No automated test asserts the atomic-write *mechanism* (temp file creation then rename). Covered indirectly: every other test reads valid JSON from `ipc.json` even when the file is being rewritten on each tick. Adding a wrapped `Storage` to count rename-vs-write ops isn't worth the complexity.
- Disconnect preservation is unit-tested end-to-end via `ComputeOutput` (Task 5) but not via the live component path — the live version would require a fake `MultiplayerClient.Room`, which is heavier than the behavior being tested warrants. The two pieces (state machine and tick wiring) are small enough that an integration failure would show up in the existing `TestFileUpdatesWhenScoresChange` test.
- Dirty-skip is exercised implicitly (the two failing writes in a row case is rare) but not assertion-tested. Add such a test later only if we observe wasted disk churn.

---

## Execution

Plan complete and saved to `docs/superpowers/plans/2026-04-17-tournament-multiplayer-ipc-producer.md`.
