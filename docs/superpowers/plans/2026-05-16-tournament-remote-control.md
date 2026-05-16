# Tournament Remote Control (HTTP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Embed a small HTTP API in the osu! tournament client so Bitfocus Companion can switch screens, manage the multiplayer connection, accept/dismiss invites, increment match scores, and poll basic state.

**Architecture:** A `TournamentRemoteControl` `Component` is added to `TournamentGame` after `BracketLoadTask` resolves. It composes a pure `RemoteControlHandler` (routing + response building, exhaustively unit-tested without any I/O) with a thin `RemoteControlListener` (one `System.Net.HttpListener`, one background thread). Game-state mutations are dispatched from the listener thread to the framework update thread via `Scheduler` + `TaskCompletionSource<T>` so I/O never blocks the update thread. Config (enable / bind address / port) lives in `tournament.ini` via the existing `TournamentConfigManager`.

**Tech Stack:** C# / .NET 8, osu!framework, `System.Net.HttpListener`, Newtonsoft.Json (already a project dependency), NUnit + the existing `osu.Game.Tournament.Tests` setup.

**Spec:** `docs/superpowers/specs/2026-05-16-tournament-remote-control-design.md`

---

## File structure

New files (create in this order):

- `osu.Game.Tournament/RemoteControl/RemoteControlResponse.cs` — value type for `(int StatusCode, string JsonBody)`.
- `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs` — pure routing + dispatching, takes callbacks for every game-state effect. Returns `Task<RemoteControlResponse>` per request. Has no `Drawable` dependency.
- `osu.Game.Tournament/RemoteControl/StatusSnapshot.cs` — immutable records used as the GET `/status` response payload (snapshotted on the update thread; serialized on the listener thread).
- `osu.Game.Tournament/RemoteControl/RemoteControlListener.cs` — owns the `HttpListener`, a background thread, and the read-request → call-handler → write-response loop. Lifecycle: `Start()` / `Dispose()`.
- `osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs` — the `Component` glue. Reads config, resolves `TournamentSceneManager` / `LadderInfo` / `MatchIPCInfo`, builds a `RemoteControlHandler` whose callbacks dispatch onto its `Scheduler`, and starts the listener.

Modified files:

- `osu.Game.Tournament/Configuration/TournamentConfigManager.cs` — add three `StorageConfig` keys + defaults.
- `osu.Game.Tournament/TournamentGame.cs` — add `new TournamentRemoteControl()` into the BracketLoadTask continuation's drawable list.

Test files:

- `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs` — pure NUnit tests for the handler (no HTTP, no game context).
- `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlListenerTest.cs` — spins up a real `RemoteControlListener` on `127.0.0.1:0`, drives it with `HttpClient`.

---

### Task 1: Add remote control configuration keys

**Files:**
- Modify: `osu.Game.Tournament/Configuration/TournamentConfigManager.cs`

- [ ] **Step 1: Add the three keys to the `StorageConfig` enum.**

Open `osu.Game.Tournament/Configuration/TournamentConfigManager.cs` and replace the enum at the bottom of the file:

```csharp
public enum StorageConfig
{
    CurrentTournament,
    RemoteControlEnabled,
    RemoteControlBindAddress,
    RemoteControlPort,
}
```

- [ ] **Step 2: Add the defaults inside `InitialiseDefaults()`.**

Inside the existing `InitialiseDefaults()` method, after the existing `SetDefault(StorageConfig.CurrentTournament, default_tournament);` line, add:

```csharp
SetDefault(StorageConfig.RemoteControlEnabled, false);
SetDefault(StorageConfig.RemoteControlBindAddress, @"127.0.0.1");
SetDefault(StorageConfig.RemoteControlPort, 7270);
```

- [ ] **Step 3: Build and confirm the project still compiles.**

Run from the repo root:

```powershell
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj
```

Expected: build succeeds, no errors. (No tests yet for this change — config keys are exercised by Task 9.)

- [ ] **Step 4: Commit.**

```bash
git add osu.Game.Tournament/Configuration/TournamentConfigManager.cs
git commit -m "add remote control config keys (enabled, bind address, port)"
```

---

### Task 2: Scaffold `RemoteControlResponse`, `RemoteControlHandler`, and 404/405 routing

**Files:**
- Create: `osu.Game.Tournament/RemoteControl/RemoteControlResponse.cs`
- Create: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

- [ ] **Step 1: Write the failing test.**

Create the test directory and file `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Tournament.RemoteControl;

namespace osu.Game.Tournament.Tests.NonVisual.RemoteControl
{
    [TestFixture]
    public class RemoteControlHandlerTest
    {
        [Test]
        public async Task UnknownRoute_Returns404()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            var response = await handler.Handle("GET", "/nope", null);

            Assert.That(response.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task WrongMethod_Returns405()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            // /status is a known GET route; POST is not allowed.
            var response = await handler.Handle("POST", "/status", null);

            Assert.That(response.StatusCode, Is.EqualTo(405));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: build error — `RemoteControlHandler` and `RemoteControlResponse` do not yet exist.

- [ ] **Step 3: Create `RemoteControlResponse.cs`.**

Create `osu.Game.Tournament/RemoteControl/RemoteControlResponse.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Tournament.RemoteControl
{
    public readonly record struct RemoteControlResponse(int StatusCode, string JsonBody)
    {
        public static RemoteControlResponse Ok() => new RemoteControlResponse(200, @"{""ok"":true}");

        public static RemoteControlResponse OkWith(string jsonBody) => new RemoteControlResponse(200, jsonBody);

        public static RemoteControlResponse Error(int statusCode, string message) =>
            new RemoteControlResponse(statusCode, $@"{{""ok"":false,""error"":{Newtonsoft.Json.JsonConvert.ToString(message)}}}");
    }
}
```

- [ ] **Step 4: Create `RemoteControlHandler.cs` with the minimum routing skeleton.**

Create `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Pure routing + dispatch for the remote control HTTP API. Game-state effects are
    /// delegated to the callbacks supplied by <see cref="TournamentRemoteControl"/>, so
    /// the handler can be exercised in unit tests with no Drawable / I/O context.
    /// </summary>
    public class RemoteControlHandler
    {
        public class Callbacks
        {
            // Filled in by later tasks.
        }

        private readonly Callbacks callbacks;

        public RemoteControlHandler(Callbacks callbacks)
        {
            this.callbacks = callbacks;
        }

        public Task<RemoteControlResponse> Handle(string method, string path, string? requestBody)
        {
            if (path == "/status")
            {
                if (method != "GET")
                    return Task.FromResult(RemoteControlResponse.Error(405, "method not allowed"));

                return Task.FromResult(RemoteControlResponse.Error(500, "status not yet implemented"));
            }

            return Task.FromResult(RemoteControlResponse.Error(404, "unknown route"));
        }
    }
}
```

- [ ] **Step 5: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: both tests pass.

- [ ] **Step 6: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlResponse.cs osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "scaffold RemoteControlHandler with 404/405 routing"
```

---

### Task 3: Implement `POST /screen/{name}`

**Files:**
- Modify: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

- [ ] **Step 1: Add the failing tests.**

Append to the `RemoteControlHandlerTest` class (above the closing brace):

```csharp
[Test]
public async Task ScreenSwitch_KnownName_InvokesCallbackAndReturns200()
{
    System.Type? captured = null;

    var callbacks = new RemoteControlHandler.Callbacks
    {
        SwitchScreen = type =>
        {
            captured = type;
            return Task.FromResult(true);
        },
    };

    var handler = new RemoteControlHandler(callbacks);

    var response = await handler.Handle("POST", "/screen/setup", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(captured, Is.EqualTo(typeof(Screens.Setup.SetupScreen)));
}

[Test]
public async Task ScreenSwitch_UnknownName_Returns400()
{
    var callbacks = new RemoteControlHandler.Callbacks
    {
        SwitchScreen = _ => Task.FromResult(true),
    };

    var handler = new RemoteControlHandler(callbacks);

    var response = await handler.Handle("POST", "/screen/banana", null);

    Assert.That(response.StatusCode, Is.EqualTo(400));
}

[Test]
public async Task ScreenSwitch_WrongMethod_Returns405()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        SwitchScreen = _ => Task.FromResult(true),
    });

    var response = await handler.Handle("GET", "/screen/setup", null);

    Assert.That(response.StatusCode, Is.EqualTo(405));
}
```

Add the using `using osu.Game.Tournament.Screens.Setup;` at the top of the file (or leave the fully-qualified reference).

- [ ] **Step 2: Run the tests to confirm they fail.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: build error — `Callbacks.SwitchScreen` doesn't exist.

- [ ] **Step 3: Extend `Callbacks` and add the route in `RemoteControlHandler.cs`.**

Replace the body of `RemoteControlHandler.cs` with:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Game.Tournament.Screens.Drawings;
using osu.Game.Tournament.Screens.Editors;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Ladder;
using osu.Game.Tournament.Screens.MapPool;
using osu.Game.Tournament.Screens.Schedule;
using osu.Game.Tournament.Screens.Setup;
using osu.Game.Tournament.Screens.Showcase;
using osu.Game.Tournament.Screens.TeamIntro;
using osu.Game.Tournament.Screens.TeamWin;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Pure routing + dispatch for the remote control HTTP API. Game-state effects are
    /// delegated to the callbacks supplied by <see cref="TournamentRemoteControl"/>, so
    /// the handler can be exercised in unit tests with no Drawable / I/O context.
    /// </summary>
    public class RemoteControlHandler
    {
        public class Callbacks
        {
            /// <summary>
            /// Switch the active screen. Return false from the lambda if the type is unknown
            /// to the scene manager; the handler will then return 400. The callback runs on
            /// the framework update thread.
            /// </summary>
            public Func<Type, Task<bool>> SwitchScreen { get; init; } = _ => Task.FromResult(false);
        }

        private static readonly IReadOnlyDictionary<string, Type> screen_types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["setup"] = typeof(SetupScreen),
            ["schedule"] = typeof(ScheduleScreen),
            ["ladder"] = typeof(LadderScreen),
            ["ladder-editor"] = typeof(LadderEditorScreen),
            ["team-editor"] = typeof(TeamEditorScreen),
            ["round-editor"] = typeof(RoundEditorScreen),
            ["showcase"] = typeof(ShowcaseScreen),
            ["mappool"] = typeof(MapPoolScreen),
            ["teamintro"] = typeof(TeamIntroScreen),
            ["seeding"] = typeof(SeedingScreen),
            ["drawings"] = typeof(DrawingsScreen),
            ["gameplay"] = typeof(GameplayScreen),
            ["teamwin"] = typeof(TeamWinScreen),
        };

        private readonly Callbacks callbacks;

        public RemoteControlHandler(Callbacks callbacks)
        {
            this.callbacks = callbacks;
        }

        public async Task<RemoteControlResponse> Handle(string method, string path, string? requestBody)
        {
            if (path.StartsWith("/screen/", StringComparison.Ordinal))
            {
                if (method != "POST")
                    return RemoteControlResponse.Error(405, "method not allowed");

                string name = path.Substring("/screen/".Length);

                if (!screen_types.TryGetValue(name, out var type))
                    return RemoteControlResponse.Error(400, $"unknown screen '{name}'");

                bool ok = await callbacks.SwitchScreen(type).ConfigureAwait(false);
                return ok ? RemoteControlResponse.Ok() : RemoteControlResponse.Error(500, "screen switch failed");
            }

            if (path == "/status")
            {
                if (method != "GET")
                    return RemoteControlResponse.Error(405, "method not allowed");

                return RemoteControlResponse.Error(500, "status not yet implemented");
            }

            return RemoteControlResponse.Error(404, "unknown route");
        }
    }
}
```

- [ ] **Step 4: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: all five tests pass.

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "add POST /screen/{name} routing to RemoteControlHandler"
```

---

### Task 4: Implement `POST /match/score/{red|blue}/increment`

**Files:**
- Modify: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

The callback returns an `int?` — the new score after increment, or `null` if there is no current match (→ 409).

- [ ] **Step 1: Add failing tests.**

Append inside `RemoteControlHandlerTest`:

```csharp
[Test]
public async Task ScoreIncrement_Red_InvokesCallbackAndReturns200()
{
    string? side = null;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        IncrementMatchScore = team =>
        {
            side = team;
            return Task.FromResult<int?>(1);
        },
    });

    var response = await handler.Handle("POST", "/match/score/red/increment", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(side, Is.EqualTo("red"));
}

[Test]
public async Task ScoreIncrement_Blue_InvokesCallbackAndReturns200()
{
    string? side = null;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        IncrementMatchScore = team =>
        {
            side = team;
            return Task.FromResult<int?>(1);
        },
    });

    var response = await handler.Handle("POST", "/match/score/blue/increment", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(side, Is.EqualTo("blue"));
}

[Test]
public async Task ScoreIncrement_NoCurrentMatch_Returns409()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        IncrementMatchScore = _ => Task.FromResult<int?>(null),
    });

    var response = await handler.Handle("POST", "/match/score/red/increment", null);

    Assert.That(response.StatusCode, Is.EqualTo(409));
}
```

- [ ] **Step 2: Run the tests to confirm they fail.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: build error — `Callbacks.IncrementMatchScore` not defined.

- [ ] **Step 3: Add the callback and route in `RemoteControlHandler.cs`.**

Inside the `Callbacks` class, after `SwitchScreen`, add:

```csharp
/// <summary>
/// Increment the current match score for the given side ("red" or "blue").
/// Returns the new score, or null if there is no current match.
/// </summary>
public Func<string, Task<int?>> IncrementMatchScore { get; init; } = _ => Task.FromResult<int?>(null);
```

Inside `Handle()`, before the `/status` block, add:

```csharp
if (path == "/match/score/red/increment" || path == "/match/score/blue/increment")
{
    if (method != "POST")
        return RemoteControlResponse.Error(405, "method not allowed");

    string side = path == "/match/score/red/increment" ? "red" : "blue";

    int? newScore = await callbacks.IncrementMatchScore(side).ConfigureAwait(false);
    if (newScore == null)
        return RemoteControlResponse.Error(409, "no current match");

    return RemoteControlResponse.Ok();
}
```

- [ ] **Step 4: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: all eight tests pass.

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "add POST /match/score/{red|blue}/increment routing"
```

---

### Task 5: Implement multiplayer invite endpoints (`/multiplayer/invite/{accept|dismiss}`)

**Files:**
- Modify: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

The callbacks return a tri-state `InviteResult` (`Accepted`, `NotAvailable`, `NoInvite`) so the handler can choose between 200 / 503 / 409 cleanly.

- [ ] **Step 1: Add failing tests.**

Append inside `RemoteControlHandlerTest`:

```csharp
[Test]
public async Task InviteAccept_HappyPath_Returns200()
{
    bool called = false;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        AcceptPendingInvite = () =>
        {
            called = true;
            return Task.FromResult(RemoteControlHandler.InviteResult.Accepted);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(called, Is.True);
}

[Test]
public async Task InviteAccept_MultiplayerUnavailable_Returns503()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        AcceptPendingInvite = () => Task.FromResult(RemoteControlHandler.InviteResult.NotAvailable),
    });

    var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

    Assert.That(response.StatusCode, Is.EqualTo(503));
}

[Test]
public async Task InviteAccept_NoInvite_Returns409()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        AcceptPendingInvite = () => Task.FromResult(RemoteControlHandler.InviteResult.NoInvite),
    });

    var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

    Assert.That(response.StatusCode, Is.EqualTo(409));
}

[Test]
public async Task InviteDismiss_HappyPath_Returns200()
{
    bool called = false;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        DismissPendingInvite = () =>
        {
            called = true;
            return Task.FromResult(RemoteControlHandler.InviteResult.Accepted);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/invite/dismiss", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(called, Is.True);
}
```

- [ ] **Step 2: Run the tests to confirm they fail.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: build error — `InviteResult`, `AcceptPendingInvite`, `DismissPendingInvite` not defined.

- [ ] **Step 3: Extend `RemoteControlHandler.cs`.**

Inside the `RemoteControlHandler` class (above the `Callbacks` class), add:

```csharp
public enum InviteResult
{
    Accepted,
    NotAvailable, // running with FileBasedIPC — multiplayer endpoint unavailable
    NoInvite,     // no pending invite to act on
}
```

Inside `Callbacks`, add:

```csharp
/// <summary>
/// Accept the current pending invite. Returns NotAvailable when multiplayer IPC
/// isn't in use, NoInvite when there's nothing pending, otherwise Accepted.
/// </summary>
public Func<Task<InviteResult>> AcceptPendingInvite { get; init; } = () => Task.FromResult(InviteResult.NotAvailable);

/// <summary>
/// Dismiss the current pending invite. Same result semantics as <see cref="AcceptPendingInvite"/>.
/// </summary>
public Func<Task<InviteResult>> DismissPendingInvite { get; init; } = () => Task.FromResult(InviteResult.NotAvailable);
```

Inside `Handle()`, before the `/status` block, add:

```csharp
if (path == "/multiplayer/invite/accept" || path == "/multiplayer/invite/dismiss")
{
    if (method != "POST")
        return RemoteControlResponse.Error(405, "method not allowed");

    var result = path == "/multiplayer/invite/accept"
        ? await callbacks.AcceptPendingInvite().ConfigureAwait(false)
        : await callbacks.DismissPendingInvite().ConfigureAwait(false);

    return result switch
    {
        InviteResult.Accepted => RemoteControlResponse.Ok(),
        InviteResult.NotAvailable => RemoteControlResponse.Error(503, "multiplayer not available"),
        InviteResult.NoInvite => RemoteControlResponse.Error(409, "no pending invite"),
        _ => RemoteControlResponse.Error(500, "unexpected result"),
    };
}
```

- [ ] **Step 4: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: all twelve tests pass.

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "add POST /multiplayer/invite/{accept|dismiss} routing"
```

---

### Task 6: Implement `/multiplayer/{connect|disconnect|reconnect}`

**Files:**
- Modify: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

Connection callbacks return a `ConnectionResult` enum so the handler can distinguish:
- `Ok` → 200
- `NotAvailable` → 503 (file-based IPC)
- `WrongState` → 409 (e.g. disconnect when not connected, connect when already connected)

`/multiplayer/connect` parses `roomId` (required) and `password` (optional) from either a JSON body (`{"roomId": 123, "password": "..."}`) or a query string (`?roomId=123&password=...`). Per the spec, when both are present the JSON body wins.

- [ ] **Step 1: Add failing tests.**

Append inside `RemoteControlHandlerTest`:

```csharp
[Test]
public async Task Connect_FromJsonBody_InvokesCallback()
{
    long? roomId = null;
    string? password = null;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (r, p) =>
        {
            roomId = r;
            password = p;
            return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/connect", @"{""roomId"":42,""password"":""hunter2""}");

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(roomId, Is.EqualTo(42));
    Assert.That(password, Is.EqualTo("hunter2"));
}

[Test]
public async Task Connect_FromQueryString_InvokesCallback()
{
    long? roomId = null;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (r, _) =>
        {
            roomId = r;
            return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(roomId, Is.EqualTo(99));
}

[Test]
public async Task Connect_BodyOverridesQuery()
{
    long? roomId = null;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (r, _) =>
        {
            roomId = r;
            return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", @"{""roomId"":42}");

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(roomId, Is.EqualTo(42));
}

[Test]
public async Task Connect_MissingRoomId_Returns400()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.Ok),
    });

    var response = await handler.Handle("POST", "/multiplayer/connect", null);

    Assert.That(response.StatusCode, Is.EqualTo(400));
}

[Test]
public async Task Connect_AlreadyConnected_Returns409()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.WrongState),
    });

    var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

    Assert.That(response.StatusCode, Is.EqualTo(409));
}

[Test]
public async Task Connect_NotAvailable_Returns503()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.NotAvailable),
    });

    var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

    Assert.That(response.StatusCode, Is.EqualTo(503));
}

[Test]
public async Task Disconnect_HappyPath_Returns200()
{
    bool called = false;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Disconnect = () =>
        {
            called = true;
            return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/disconnect", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(called, Is.True);
}

[Test]
public async Task Disconnect_NotConnected_Returns409()
{
    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Disconnect = () => Task.FromResult(RemoteControlHandler.ConnectionResult.WrongState),
    });

    var response = await handler.Handle("POST", "/multiplayer/disconnect", null);

    Assert.That(response.StatusCode, Is.EqualTo(409));
}

[Test]
public async Task Reconnect_HappyPath_Returns200()
{
    bool called = false;

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        Reconnect = () =>
        {
            called = true;
            return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
        },
    });

    var response = await handler.Handle("POST", "/multiplayer/reconnect", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(called, Is.True);
}
```

- [ ] **Step 2: Run the tests to confirm they fail.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: build error — `ConnectionResult`, `Connect`, `Disconnect`, `Reconnect` not defined.

- [ ] **Step 3: Extend `RemoteControlHandler.cs`.**

Add at the top of the file:

```csharp
using System.Collections.Specialized;
using System.Web;
```

Inside the class (next to `InviteResult`), add:

```csharp
public enum ConnectionResult
{
    Ok,
    NotAvailable, // file-based IPC
    WrongState,   // connect-when-connected, disconnect/reconnect-when-not-connected, etc.
}
```

Inside `Callbacks`, add:

```csharp
public Func<long, string?, Task<ConnectionResult>> Connect { get; init; } =
    (_, _) => Task.FromResult(ConnectionResult.NotAvailable);

public Func<Task<ConnectionResult>> Disconnect { get; init; } = () => Task.FromResult(ConnectionResult.NotAvailable);

public Func<Task<ConnectionResult>> Reconnect { get; init; } = () => Task.FromResult(ConnectionResult.NotAvailable);
```

Because `path` may include a query string for `/multiplayer/connect`, the method now splits the query off first. Replace the **entire** existing `Handle()` method with the version below (which preserves every route block added in Tasks 2-5 and adds the new connect/disconnect/reconnect blocks plus a shared helper at the end). The `/status` block at the end is unchanged from Task 2 and will be replaced again in Task 7:

```csharp
public async Task<RemoteControlResponse> Handle(string method, string path, string? requestBody)
{
    string queryString = "";
    int qIdx = path.IndexOf('?');
    if (qIdx >= 0)
    {
        queryString = path.Substring(qIdx + 1);
        path = path.Substring(0, qIdx);
    }

    if (path.StartsWith("/screen/", StringComparison.Ordinal))
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        string name = path.Substring("/screen/".Length);

        if (!screen_types.TryGetValue(name, out var type))
            return RemoteControlResponse.Error(400, $"unknown screen '{name}'");

        bool ok = await callbacks.SwitchScreen(type).ConfigureAwait(false);
        return ok ? RemoteControlResponse.Ok() : RemoteControlResponse.Error(500, "screen switch failed");
    }

    if (path == "/match/score/red/increment" || path == "/match/score/blue/increment")
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        string side = path == "/match/score/red/increment" ? "red" : "blue";

        int? newScore = await callbacks.IncrementMatchScore(side).ConfigureAwait(false);
        if (newScore == null)
            return RemoteControlResponse.Error(409, "no current match");

        return RemoteControlResponse.Ok();
    }

    if (path == "/multiplayer/invite/accept" || path == "/multiplayer/invite/dismiss")
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        var result = path == "/multiplayer/invite/accept"
            ? await callbacks.AcceptPendingInvite().ConfigureAwait(false)
            : await callbacks.DismissPendingInvite().ConfigureAwait(false);

        return result switch
        {
            InviteResult.Accepted => RemoteControlResponse.Ok(),
            InviteResult.NotAvailable => RemoteControlResponse.Error(503, "multiplayer not available"),
            InviteResult.NoInvite => RemoteControlResponse.Error(409, "no pending invite"),
            _ => RemoteControlResponse.Error(500, "unexpected result"),
        };
    }

    if (path == "/multiplayer/connect")
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        long? roomId = null;
        string? password = null;

        if (queryString.Length > 0)
        {
            NameValueCollection q = HttpUtility.ParseQueryString(queryString);
            if (long.TryParse(q["roomId"], out long qRoomId))
                roomId = qRoomId;
            password = q["password"];
        }

        if (!string.IsNullOrEmpty(requestBody))
        {
            try
            {
                var parsed = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(requestBody, new { roomId = (long?)null, password = (string?)null });
                if (parsed?.roomId != null) roomId = parsed.roomId;
                if (parsed?.password != null) password = parsed.password;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return RemoteControlResponse.Error(400, "malformed JSON body");
            }
        }

        if (roomId == null)
            return RemoteControlResponse.Error(400, "missing roomId");

        var connectResult = await callbacks.Connect(roomId.Value, password).ConfigureAwait(false);
        return connectionResultToResponse(connectResult);
    }

    if (path == "/multiplayer/disconnect")
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        var result = await callbacks.Disconnect().ConfigureAwait(false);
        return connectionResultToResponse(result);
    }

    if (path == "/multiplayer/reconnect")
    {
        if (method != "POST")
            return RemoteControlResponse.Error(405, "method not allowed");

        var result = await callbacks.Reconnect().ConfigureAwait(false);
        return connectionResultToResponse(result);
    }

    if (path == "/status")
    {
        if (method != "GET")
            return RemoteControlResponse.Error(405, "method not allowed");

        return RemoteControlResponse.Error(500, "status not yet implemented");
    }

    return RemoteControlResponse.Error(404, "unknown route");
}

private static RemoteControlResponse connectionResultToResponse(ConnectionResult result) => result switch
{
    ConnectionResult.Ok => RemoteControlResponse.Ok(),
    ConnectionResult.NotAvailable => RemoteControlResponse.Error(503, "multiplayer not available"),
    ConnectionResult.WrongState => RemoteControlResponse.Error(409, "wrong connection state"),
    _ => RemoteControlResponse.Error(500, "unexpected result"),
};
```

- [ ] **Step 4: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: all twenty-one tests pass.

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "add POST /multiplayer/{connect|disconnect|reconnect} routing"
```

---

### Task 7: Implement `GET /status`

**Files:**
- Create: `osu.Game.Tournament/RemoteControl/StatusSnapshot.cs`
- Modify: `osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs`
- Modify: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs`

The handler asks its callback for a `StatusSnapshot` (already populated from a snapshot taken on the update thread by `TournamentRemoteControl`), then serializes it to JSON on the listener thread. Keeping snapshot construction outside the handler keeps the handler pure.

- [ ] **Step 1: Create `StatusSnapshot.cs`.**

Create `osu.Game.Tournament/RemoteControl/StatusSnapshot.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Immutable snapshot returned by GET /status. Construction happens on the framework
    /// update thread; serialization happens on the listener thread.
    /// </summary>
    public record StatusSnapshot(
        [property: JsonProperty("currentScreen")] string? CurrentScreen,
        [property: JsonProperty("multiplayer")] MultiplayerSnapshot Multiplayer,
        [property: JsonProperty("match")] MatchSnapshot? Match
    );

    public record MultiplayerSnapshot(
        [property: JsonProperty("available")] bool Available,
        [property: JsonProperty("connected", NullValueHandling = NullValueHandling.Ignore)] bool? Connected,
        [property: JsonProperty("roomId", NullValueHandling = NullValueHandling.Ignore)] long? RoomId,
        [property: JsonProperty("pendingInvite", NullValueHandling = NullValueHandling.Ignore)] PendingInviteSnapshot? PendingInvite,
        [property: JsonProperty("tourneyState", NullValueHandling = NullValueHandling.Ignore)] string? TourneyState
    )
    {
        public static MultiplayerSnapshot Unavailable() => new MultiplayerSnapshot(false, null, null, null, null);
    }

    public record PendingInviteSnapshot(
        [property: JsonProperty("roomId")] long RoomId,
        [property: JsonProperty("inviter")] string Inviter
    );

    public record MatchSnapshot(
        [property: JsonProperty("team1Score")] int? Team1Score,
        [property: JsonProperty("team2Score")] int? Team2Score,
        [property: JsonProperty("team1Acronym")] string? Team1Acronym,
        [property: JsonProperty("team2Acronym")] string? Team2Acronym
    );
}
```

- [ ] **Step 2: Add failing tests.**

Append inside `RemoteControlHandlerTest`:

```csharp
[Test]
public async Task Status_Available_ReturnsExpectedShape()
{
    var snapshot = new StatusSnapshot(
        CurrentScreen: "GameplayScreen",
        Multiplayer: new MultiplayerSnapshot(
            Available: true,
            Connected: true,
            RoomId: 123,
            PendingInvite: null,
            TourneyState: "Playing"),
        Match: new MatchSnapshot(2, 1, "RED", "BLU")
    );

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        GetStatus = () => Task.FromResult(snapshot),
    });

    var response = await handler.Handle("GET", "/status", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(response.JsonBody, Does.Contain(@"""currentScreen"":""GameplayScreen"""));
    Assert.That(response.JsonBody, Does.Contain(@"""available"":true"));
    Assert.That(response.JsonBody, Does.Contain(@"""roomId"":123"));
    Assert.That(response.JsonBody, Does.Contain(@"""team1Score"":2"));
}

[Test]
public async Task Status_Unavailable_OmitsMultiplayerDetails()
{
    var snapshot = new StatusSnapshot(
        CurrentScreen: "SetupScreen",
        Multiplayer: MultiplayerSnapshot.Unavailable(),
        Match: null
    );

    var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
    {
        GetStatus = () => Task.FromResult(snapshot),
    });

    var response = await handler.Handle("GET", "/status", null);

    Assert.That(response.StatusCode, Is.EqualTo(200));
    Assert.That(response.JsonBody, Does.Contain(@"""available"":false"));
    Assert.That(response.JsonBody, Does.Not.Contain(@"""connected"""));
    Assert.That(response.JsonBody, Does.Not.Contain(@"""roomId"""));
}
```

- [ ] **Step 3: Extend `Callbacks` and replace the `/status` placeholder in `RemoteControlHandler.cs`.**

Inside `Callbacks`, add:

```csharp
public Func<Task<StatusSnapshot>> GetStatus { get; init; } = () => Task.FromResult(
    new StatusSnapshot(null, MultiplayerSnapshot.Unavailable(), null));
```

Replace the `/status` block inside `Handle()` with:

```csharp
if (path == "/status")
{
    if (method != "GET")
        return RemoteControlResponse.Error(405, "method not allowed");

    var snapshot = await callbacks.GetStatus().ConfigureAwait(false);
    string body = Newtonsoft.Json.JsonConvert.SerializeObject(snapshot,
        new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore });
    return RemoteControlResponse.OkWith(body);
}
```

- [ ] **Step 4: Run the tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlHandlerTest"
```

Expected: all twenty-three tests pass.

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/StatusSnapshot.cs osu.Game.Tournament/RemoteControl/RemoteControlHandler.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlHandlerTest.cs
git commit -m "add GET /status endpoint and StatusSnapshot record types"
```

---

### Task 8: Build the HTTP listener and write its integration test

**Files:**
- Create: `osu.Game.Tournament/RemoteControl/RemoteControlListener.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlListenerTest.cs`

The listener wraps `HttpListener`, runs a single background accept loop, calls the handler, writes the response, and gracefully exits on `Dispose`. Bind failures are logged and **do not throw** to the caller — they leave the listener in a non-running state.

- [ ] **Step 1: Create the integration test (which will fail because the class doesn't exist yet).**

Create `osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlListenerTest.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Tournament.RemoteControl;

namespace osu.Game.Tournament.Tests.NonVisual.RemoteControl
{
    [TestFixture]
    public class RemoteControlListenerTest
    {
        private static int pickFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Test]
        public async Task RoundTrip_UnknownRoute_Returns404()
        {
            int port = pickFreePort();
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            using var listener = new RemoteControlListener("127.0.0.1", port, handler);
            listener.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/nope");

            Assert.That((int)response.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task RoundTrip_PostJsonBody_IsPassedToHandler()
        {
            int port = pickFreePort();
            string? receivedBody = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (roomId, _) =>
                {
                    receivedBody = $"roomId={roomId}";
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            using var listener = new RemoteControlListener("127.0.0.1", port, handler);
            listener.Start();

            using var client = new HttpClient();
            var response = await client.PostAsync(
                $"http://127.0.0.1:{port}/multiplayer/connect",
                new StringContent(@"{""roomId"":99}", Encoding.UTF8, "application/json"));

            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(receivedBody, Is.EqualTo("roomId=99"));
        }

        [Test]
        public void Start_FailedBind_DoesNotThrow()
        {
            // Occupy a port, then try to bind a listener to the same port.
            int port = pickFreePort();

            var occupier = new TcpListener(IPAddress.Loopback, port);
            occupier.Start();

            try
            {
                var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());
                using var listener = new RemoteControlListener("127.0.0.1", port, handler);

                Assert.DoesNotThrow(() => listener.Start());
                Assert.That(listener.IsRunning, Is.False);
            }
            finally
            {
                occupier.Stop();
            }
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlListenerTest"
```

Expected: build error — `RemoteControlListener` does not yet exist.

- [ ] **Step 3: Create `RemoteControlListener.cs`.**

Create `osu.Game.Tournament/RemoteControl/RemoteControlListener.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Hosts a <see cref="HttpListener"/> on a single dedicated thread. Calls
    /// <see cref="RemoteControlHandler.Handle"/> for each request and writes the response.
    /// Bind failures are logged and leave the listener in a non-running state — they never throw.
    /// </summary>
    public sealed class RemoteControlListener : IDisposable
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly RemoteControlHandler handler;

        private HttpListener? listener;
        private Thread? loopThread;
        private CancellationTokenSource? cancellation;

        public bool IsRunning { get; private set; }

        public RemoteControlListener(string bindAddress, int port, RemoteControlHandler handler)
        {
            this.bindAddress = bindAddress;
            this.port = port;
            this.handler = handler;
        }

        public void Start()
        {
            if (IsRunning)
                return;

            listener = new HttpListener();
            listener.Prefixes.Add($"http://{bindAddress}:{port}/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException e)
            {
                Logger.Log($"[RemoteControl] Failed to bind to {bindAddress}:{port}: {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
                listener = null;
                return;
            }

            cancellation = new CancellationTokenSource();
            loopThread = new Thread(loop) { IsBackground = true, Name = $"RemoteControl-{port}" };
            loopThread.Start();
            IsRunning = true;

            Logger.Log($"[RemoteControl] Listening on http://{bindAddress}:{port}/", LoggingTarget.Runtime, LogLevel.Important);
        }

        private void loop()
        {
            while (!cancellation!.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = listener!.GetContext();
                }
                catch (HttpListenerException)
                {
                    // listener was stopped — exit cleanly.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    handleContext(context);
                }
                catch (Exception e)
                {
                    Logger.Log($"[RemoteControl] Unhandled exception: {e}", LoggingTarget.Runtime, LogLevel.Important);

                    try
                    {
                        writeResponse(context, RemoteControlResponse.Error(500, e.Message));
                    }
                    catch
                    {
                        // best-effort — the connection may already be torn down.
                    }
                }
            }
        }

        private void handleContext(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            string path = context.Request.Url?.PathAndQuery ?? "/";
            string? body = null;

            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                body = reader.ReadToEnd();
            }

            // Block the listener thread on the handler. The handler dispatches to the
            // update thread internally via its callbacks; it must complete (or time out)
            // before we can write the response.
            var handleTask = handler.Handle(method, path, body);

            if (!handleTask.Wait(2500))
            {
                writeResponse(context, RemoteControlResponse.Error(504, "request timed out"));
                return;
            }

            writeResponse(context, handleTask.Result);
        }

        private static void writeResponse(HttpListenerContext context, RemoteControlResponse response)
        {
            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";

            byte[] bytes = Encoding.UTF8.GetBytes(response.JsonBody);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        public void Dispose()
        {
            if (!IsRunning)
            {
                listener?.Close();
                return;
            }

            IsRunning = false;
            cancellation?.Cancel();

            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch
            {
                // already torn down.
            }

            loopThread?.Join(1000);
            cancellation?.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run the listener tests.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControlListenerTest"
```

Expected: all three tests pass.

- [ ] **Step 5: Run the full handler tests too, to make sure nothing regressed.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter "FullyQualifiedName~RemoteControl"
```

Expected: all twenty-six tests pass (23 handler + 3 listener).

- [ ] **Step 6: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/RemoteControlListener.cs osu.Game.Tournament.Tests/NonVisual/RemoteControl/RemoteControlListenerTest.cs
git commit -m "add RemoteControlListener wrapping HttpListener with bind-failure resilience"
```

---

### Task 9: Build the `TournamentRemoteControl` component that wires callbacks to game state

**Files:**
- Create: `osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs`

The component is a `Component` (Drawable, no visual output). In `BackgroundDependencyLoader` it resolves `TournamentSceneManager`, `LadderInfo`, `MatchIPCInfo`, the `Storage` (for `tournament.ini`) and constructs a `TournamentConfigManager` to read the three remote-control settings. It builds a `RemoteControlHandler.Callbacks` whose every callback dispatches the actual mutation onto the update thread using `Scheduler` + `TaskCompletionSource<T>`.

There are no unit tests for this class — it's the integration glue. End-to-end behavior is validated by `RemoteControlListenerTest` (Task 8) and manual verification (Task 11).

- [ ] **Step 1: Create the file.**

Create `osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Bridges <see cref="RemoteControlHandler"/> and <see cref="RemoteControlListener"/> to the
    /// live tournament client. All game-state mutations are dispatched onto the framework
    /// update thread via the component's <see cref="Drawable.Scheduler"/>.
    /// </summary>
    public partial class TournamentRemoteControl : Component
    {
        private RemoteControlListener? listener;

        [Resolved]
        private TournamentSceneManager sceneManager { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            using var config = new TournamentConfigManager(storage);

            if (!config.Get<bool>(StorageConfig.RemoteControlEnabled))
                return;

            string bindAddress = config.Get<string>(StorageConfig.RemoteControlBindAddress);
            int port = config.Get<int>(StorageConfig.RemoteControlPort);

            var multiplayerIpc = ipc as MultiplayerMatchIPCInfo;

            var callbacks = new RemoteControlHandler.Callbacks
            {
                SwitchScreen = type => dispatch(() =>
                {
                    sceneManager.SetScreen(type);
                    return true;
                }),

                IncrementMatchScore = side => dispatch<int?>(() =>
                {
                    var match = ladder.CurrentMatch.Value;
                    if (match == null)
                        return null;

                    if (side == "red")
                    {
                        int newValue = (match.Team1Score.Value ?? 0) + 1;
                        match.Team1Score.Value = newValue;
                        return newValue;
                    }
                    else
                    {
                        int newValue = (match.Team2Score.Value ?? 0) + 1;
                        match.Team2Score.Value = newValue;
                        return newValue;
                    }
                }),

                AcceptPendingInvite = () => dispatch(() =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.InviteResult.NotAvailable;
                    if (multiplayerIpc.PendingInvite.Value == null)
                        return RemoteControlHandler.InviteResult.NoInvite;

                    multiplayerIpc.AcceptPendingInvite();
                    return RemoteControlHandler.InviteResult.Accepted;
                }),

                DismissPendingInvite = () => dispatch(() =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.InviteResult.NotAvailable;
                    if (multiplayerIpc.PendingInvite.Value == null)
                        return RemoteControlHandler.InviteResult.NoInvite;

                    multiplayerIpc.DismissPendingInvite();
                    return RemoteControlHandler.InviteResult.Accepted;
                }),

                Connect = (roomId, password) => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    try
                    {
                        await multiplayerIpc.Connect(roomId, password).ConfigureAwait(false);
                        return RemoteControlHandler.ConnectionResult.Ok;
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[RemoteControl] Connect failed: {e.Message}", LoggingTarget.Runtime, LogLevel.Important);
                        return RemoteControlHandler.ConnectionResult.WrongState;
                    }
                }),

                Disconnect = () => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (!multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    await multiplayerIpc.Disconnect().ConfigureAwait(false);
                    return RemoteControlHandler.ConnectionResult.Ok;
                }),

                Reconnect = () => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (!multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    await multiplayerIpc.Reconnect().ConfigureAwait(false);
                    return RemoteControlHandler.ConnectionResult.Ok;
                }),

                GetStatus = () => dispatch(() => buildStatus(multiplayerIpc)),
            };

            var handler = new RemoteControlHandler(callbacks);

            listener = new RemoteControlListener(bindAddress, port, handler);
            listener.Start();
        }

        private StatusSnapshot buildStatus(MultiplayerMatchIPCInfo? multiplayerIpc)
        {
            string? currentScreen = sceneManager.CurrentScreen?.GetType().Name;

            MultiplayerSnapshot mp;

            if (multiplayerIpc == null)
            {
                mp = MultiplayerSnapshot.Unavailable();
            }
            else
            {
                PendingInviteSnapshot? invite = multiplayerIpc.PendingInvite.Value == null
                    ? null
                    : new PendingInviteSnapshot(multiplayerIpc.PendingInvite.Value.RoomId, multiplayerIpc.PendingInvite.Value.InviterName);

                mp = new MultiplayerSnapshot(
                    Available: true,
                    Connected: multiplayerIpc.IsConnected.Value,
                    RoomId: multiplayerIpc.ConnectedRoomId.Value,
                    PendingInvite: invite,
                    TourneyState: multiplayerIpc.State.Value.ToString());
            }

            MatchSnapshot? match = null;

            if (ladder.CurrentMatch.Value is TournamentMatch current)
            {
                match = new MatchSnapshot(
                    Team1Score: current.Team1Score.Value,
                    Team2Score: current.Team2Score.Value,
                    Team1Acronym: current.Team1.Value?.Acronym.Value,
                    Team2Acronym: current.Team2.Value?.Acronym.Value);
            }

            return new StatusSnapshot(currentScreen, mp, match);
        }

        private Task<T> dispatch<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Schedule(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            return tcs.Task;
        }

        private Task<T> dispatchAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Schedule(() =>
            {
                action().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.SetException(t.Exception!.GetBaseException());
                    else
                        tcs.SetResult(t.Result);
                });
            });

            return tcs.Task;
        }

        protected override void Dispose(bool isDisposing)
        {
            listener?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
```

- [ ] **Step 2: Confirm `TournamentSceneManager` exposes `CurrentScreen` publicly.**

Open `osu.Game.Tournament/TournamentSceneManager.cs` and check whether `currentScreen` is private (it currently is). The status snapshot reads it, so the field must be exposed.

Edit `osu.Game.Tournament/TournamentSceneManager.cs`. Replace:

```csharp
private Drawable? currentScreen;
```

with:

```csharp
public Drawable? CurrentScreen { get; private set; }
```

Then within the file, replace every remaining reference to `currentScreen` with `CurrentScreen` (there should be five — the assignment in `SetScreen(Drawable)`, three in `SetScreen(Type)`, and the conditional in `SetScreen(Type)` checking `currentScreen == target`). Confirm with a quick grep on the file that there are no remaining lowercase `currentScreen` identifiers (other than the local capture `var lastScreen = currentScreen;` which becomes `var lastScreen = CurrentScreen;`).

- [ ] **Step 3: Build to confirm nothing else broke.**

```powershell
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Run the full test suite.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```

Expected: all existing tests still pass (including the existing handler + listener tests added in earlier tasks).

- [ ] **Step 5: Commit.**

```bash
git add osu.Game.Tournament/RemoteControl/TournamentRemoteControl.cs osu.Game.Tournament/TournamentSceneManager.cs
git commit -m "add TournamentRemoteControl component bridging handler to game state"
```

---

### Task 10: Register `TournamentRemoteControl` in `TournamentGame`

**Files:**
- Modify: `osu.Game.Tournament/TournamentGame.cs`

- [ ] **Step 1: Add `TournamentRemoteControl` to the drawable list inside the BracketLoadTask continuation.**

Open `osu.Game.Tournament/TournamentGame.cs`. Locate the `LoadComponentsAsync(new[] { … })` call inside the `BracketLoadTask.ContinueWith(...)` block.

Add `using osu.Game.Tournament.RemoteControl;` at the top.

Inside the `new[]` array passed to `LoadComponentsAsync`, append `new TournamentRemoteControl()` as the last element before the closing `}`:

```csharp
LoadComponentsAsync(new[]
{
    new SaveChangesOverlay
    {
        Depth = float.MinValue,
    },
    heightWarning = new WarningBox("Please make the window wider")
    {
        Anchor = Anchor.BottomCentre,
        Origin = Anchor.BottomCentre,
        Margin = new MarginPadding(20),
    },
    new OsuContextMenuContainer
    {
        RelativeSizeAxes = Axes.Both,
        Child = new TournamentSceneManager()
    },
    dialogOverlay,
    new TournamentRemoteControl(),
}, drawables =>
{
    // ... existing continuation unchanged ...
});
```

Note: the existing array uses an implicit `Drawable[]` element type. `TournamentRemoteControl` is a `Component` (a `Drawable`), so it fits.

- [ ] **Step 2: Build.**

```powershell
dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Run the full test suite.**

```powershell
dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 4: Commit.**

```bash
git add osu.Game.Tournament/TournamentGame.cs
git commit -m "register TournamentRemoteControl in TournamentGame"
```

---

### Task 11: Manual smoke test

This task is performed by the human operator, not the agent. It validates the feature end-to-end against a running tournament client.

- [ ] **Step 1: Edit `tournament.ini` to enable the listener.**

Open the tournament storage directory (the same directory that contains `bracket.json`) and edit `tournament.ini`. Set:

```
RemoteControlEnabled = True
RemoteControlBindAddress = 127.0.0.1
RemoteControlPort = 7270
```

If `tournament.ini` doesn't exist yet, launch the tournament client once and exit; it will be created with defaults that you can then edit.

- [ ] **Step 2: Launch the tournament client and confirm the listener started.**

Run the `osu.Desktop.Tournament` launch profile. Inside `runtime.log`, find a line like:

```
[RemoteControl] Listening on http://127.0.0.1:7270/
```

- [ ] **Step 3: Drive each endpoint with curl.**

In PowerShell (or `pwsh`):

```powershell
# Switch screens
Invoke-RestMethod -Method Post http://127.0.0.1:7270/screen/setup
Invoke-RestMethod -Method Post http://127.0.0.1:7270/screen/gameplay
Invoke-RestMethod -Method Post http://127.0.0.1:7270/screen/mappool

# Get status
Invoke-RestMethod http://127.0.0.1:7270/status | ConvertTo-Json -Depth 5

# Increment scores (requires a current match selected)
Invoke-RestMethod -Method Post http://127.0.0.1:7270/match/score/red/increment
Invoke-RestMethod -Method Post http://127.0.0.1:7270/match/score/blue/increment

# Multiplayer connect (only when running with UseMultiplayerSpectating)
Invoke-RestMethod -Method Post http://127.0.0.1:7270/multiplayer/connect `
    -ContentType "application/json" `
    -Body '{"roomId":12345,"password":"optional"}'

Invoke-RestMethod -Method Post http://127.0.0.1:7270/multiplayer/disconnect
```

Expected behavior:
- Each `/screen/...` call switches the displayed screen.
- `/status` returns the documented JSON shape.
- Score increments update the on-screen `team1Score` / `team2Score` next to the team panels.
- Multiplayer endpoints return `503` if running with file-based IPC; succeed and update the on-screen "Connected (Room X)" status if running with multiplayer spectating.

- [ ] **Step 4: Verify graceful shutdown.**

Close the tournament client window. The `runtime.log` should not show any unhandled exception from the listener thread.

- [ ] **Step 5: Verify bind failure handling.**

Start a second tournament client instance with the same `RemoteControlPort`. The second instance should log:

```
[RemoteControl] Failed to bind to 127.0.0.1:7270: ...
```

…and continue running normally (no crash, no listener active).

- [ ] **Step 6: Set up one Companion button as a final sanity check.**

In Bitfocus Companion, add a "Generic HTTP" connection. Create a button with action "POST" to `http://127.0.0.1:7270/screen/teamintro`. Pressing the button should switch the tournament client to the Team Intro screen.

- [ ] **Step 7: Manual smoke test signed off.**

When all of the above pass, remove this task's checkbox from the plan (or just check it). No commit needed unless `tournament.ini` is being committed (it usually isn't — `bracket.json` and `tournament.ini` are user-local).

---

## Spec coverage check

| Spec section | Implemented in |
|---|---|
| Configuration keys | Task 1 |
| `POST /screen/{name}` for all 13 screens | Task 3 |
| `POST /multiplayer/connect` (JSON body + query, body wins) | Task 6 |
| `POST /multiplayer/disconnect` / `reconnect` | Task 6 |
| `POST /multiplayer/invite/accept` / `dismiss` | Task 5 |
| `POST /match/score/{red\|blue}/increment` (incl. null start) | Tasks 4, 9 |
| `GET /status` (with available + unavailable variants) | Tasks 7, 9 |
| Response codes 200/400/404/405/409/500/503/504 | Tasks 2-8 |
| Threading: listener thread → `Scheduler` + `TaskCompletionSource` | Task 9 |
| 2-second dispatch timeout → `504` | Task 8 (`handleTask.Wait(2500)`) |
| Async multiplayer actions await the underlying Task | Task 9 (`dispatchAsync`) |
| Lifecycle: opt-in via config, no crash on bind failure | Tasks 8, 9 |
| Operator notes (urlacl on Windows for LAN binding) | Documented in spec; covered conceptually in Task 11 |
| Tests for routing logic | Tasks 2-7 (handler tests) |
| Tests for HTTP I/O + bind failure | Task 8 (listener tests) |
| Manual end-to-end + Companion smoke test | Task 11 |

All spec sections are covered.

## Notes for the implementer

- `TournamentSceneManager.CurrentScreen` is added by Task 9. Don't try to read it earlier.
- The `Callbacks` class uses `init` setters; every callback has a safe default that returns the "feature not available" state, so partially-configured `Callbacks` instances are still safe to use in tests.
- The listener test (`Start_FailedBind_DoesNotThrow`) deliberately uses a `TcpListener` to occupy the port rather than a second `HttpListener` because Windows `HttpListener` requires URL ACL configuration that may not exist in CI. A `TcpListener` on the same port reliably triggers an `HttpListenerException` on `HttpListener.Start()`.
- Do not introduce a NuGet dependency for the HTTP server — `System.Net.HttpListener` is built into .NET 8 and is intentionally what this design picks.
