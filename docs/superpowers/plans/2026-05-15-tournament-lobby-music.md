# Tournament Lobby Music Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the tournament overlay play the current room's beatmap track on loop while connected to a multiplayer room but not actively in gameplay (mirroring the regular client's multiplayer-lobby music behaviour).

**Architecture:** A small `Component` (`TournamentLobbyMusic`) resolves the existing `MusicController`, the global `Bindable<WorkingBeatmap>`, `BeatmapManager`, and `MultiplayerMatchIPCInfo`. It (a) keeps the global beatmap in sync with the room's current map by looking up the local `WorkingBeatmap`, (b) computes a `ShouldPlay` decision from `(isConnected, ipcState, hasResolvedBeatmap)`, and (c) calls `MusicController.EnsurePlayingSomething` / `Stop` on that decision. The decision is a pure function so we can unit-test it independently of audio plumbing. Added at `TournamentGameBase.readBracket` only when `UseMultiplayerSpectating` is on, alongside `MultiplayerIPCWriter`.

**Tech Stack:** C#, osu!Framework (Bindable/Drawable/AudioManager), Realms (for beatmap import notifications), NUnit (tests).

**Spec:** `docs/superpowers/specs/2026-05-15-tournament-lobby-music-design.md`

---

## File structure

- **Create** `osu.Game.Tournament/Components/TournamentLobbyMusic.cs` — the new component. Owns the trigger logic, the global-beatmap sync, the realm subscription, and the `MusicController` calls.
- **Modify** `osu.Game.Tournament/TournamentGameBase.cs` (around line 232, where `MultiplayerIPCWriter` is added) — register `TournamentLobbyMusic` in the same branch.
- **Create** `osu.Game.Tournament.Tests/NonVisual/TournamentLobbyMusicTest.cs` — unit-tests the `ShouldPlay` pure function for all state combinations.

The component is a single focused file (~150 lines projected). It does not split further.

---

## Task 1: Define `ShouldPlay` decision as a pure function and unit-test it

The decision is the only piece of logic with non-trivial behaviour — extract it as a static so we can test all state combinations without booting a host.

**Files:**
- Create: `osu.Game.Tournament/Components/TournamentLobbyMusic.cs`
- Create: `osu.Game.Tournament.Tests/NonVisual/TournamentLobbyMusicTest.cs`

- [x] **Step 1: Write the failing test**

Create `osu.Game.Tournament.Tests/NonVisual/TournamentLobbyMusicTest.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Tests.NonVisual
{
    [TestFixture]
    public class TournamentLobbyMusicTest
    {
        [Test]
        public void ShouldPlay_disconnected_returnsFalse()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: false, TourneyState.Idle, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void ShouldPlay_noResolvedBeatmap_returnsFalse()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: false), Is.False);
        }

        [Test]
        public void ShouldPlay_idle_returnsTrue()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Idle, hasResolvedBeatmap: true), Is.True);
        }

        [Test]
        public void ShouldPlay_waitingForClients_returnsTrue()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.WaitingForClients, hasResolvedBeatmap: true), Is.True);
        }

        [Test]
        public void ShouldPlay_playing_returnsFalse()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Playing, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void ShouldPlay_ranking_returnsFalse()
        {
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Ranking, hasResolvedBeatmap: true), Is.False);
        }

        [Test]
        public void ShouldPlay_initialising_returnsFalse()
        {
            // Initialising is never a "lobby" state — be conservative and don't play.
            Assert.That(TournamentLobbyMusic.ShouldPlay(isConnected: true, TourneyState.Initialising, hasResolvedBeatmap: true), Is.False);
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TournamentLobbyMusicTest`

Expected: build fails with `The type or namespace name 'TournamentLobbyMusic' does not exist`.

- [x] **Step 3: Create the component stub with just the `ShouldPlay` function**

Create `osu.Game.Tournament/Components/TournamentLobbyMusic.cs`:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Tournament.IPC;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Plays the current multiplayer-room beatmap on loop while the tournament
    /// overlay is connected to a room and gameplay isn't actively rendered.
    /// </summary>
    public partial class TournamentLobbyMusic : Component
    {
        /// <summary>
        /// Pure decision: should we be playing lobby music right now?
        /// Music plays only when connected to a room with a locally-resolved beatmap
        /// and the IPC state is Idle or WaitingForClients (gameplay master clock
        /// owns audio during Playing and Ranking).
        /// </summary>
        public static bool ShouldPlay(bool isConnected, TourneyState state, bool hasResolvedBeatmap)
        {
            if (!isConnected || !hasResolvedBeatmap)
                return false;

            return state == TourneyState.Idle || state == TourneyState.WaitingForClients;
        }
    }
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TournamentLobbyMusicTest`

Expected: 7 tests pass.

- [x] **Step 5: Commit**

```bash
git add osu.Game.Tournament/Components/TournamentLobbyMusic.cs osu.Game.Tournament.Tests/NonVisual/TournamentLobbyMusicTest.cs
git commit -m "add TournamentLobbyMusic ShouldPlay decision with tests"
```

---

## Task 2: Wire the global-beatmap sync and audio side effects

Now fill in the component body so that it actually drives `MusicController`.

**Files:**
- Modify: `osu.Game.Tournament/Components/TournamentLobbyMusic.cs`

- [x] **Step 1: Add the framework wiring**

Replace the body of `osu.Game.Tournament/Components/TournamentLobbyMusic.cs` with the full implementation:

```csharp
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using Realms;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Plays the current multiplayer-room beatmap on loop while the tournament
    /// overlay is connected to a room and gameplay isn't actively rendered.
    /// </summary>
    public partial class TournamentLobbyMusic : Component
    {
        [Resolved]
        private MultiplayerMatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> globalBeatmap { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        private IDisposable? realmSubscription;

        /// <summary>
        /// The online ID of the beatmap we last attempted to resolve. Read on the update thread.
        /// </summary>
        private int wantedBeatmapId;

        /// <summary>
        /// True iff the wanted beatmap has been successfully turned into a global <see cref="WorkingBeatmap"/>.
        /// </summary>
        private bool hasResolvedBeatmap;

        /// <summary>
        /// Pure decision: should we be playing lobby music right now?
        /// Music plays only when connected to a room with a locally-resolved beatmap
        /// and the IPC state is Idle or WaitingForClients (gameplay master clock
        /// owns audio during Playing and Ranking).
        /// </summary>
        public static bool ShouldPlay(bool isConnected, TourneyState state, bool hasResolvedBeatmap)
        {
            if (!isConnected || !hasResolvedBeatmap)
                return false;

            return state == TourneyState.Idle || state == TourneyState.WaitingForClients;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Retry beatmap resolution when local imports land — handles the case where
            // ipc.Beatmap is set before MultiplayerMatchIPCInfo.ensureBeatmapDownloaded
            // finishes pulling the set.
            realmSubscription = realm.RegisterForNotifications(
                r => r.All<BeatmapSetInfo>().Where(s => !s.DeletePending),
                (_, _) =>
                {
                    if (!hasResolvedBeatmap && wantedBeatmapId > 0)
                        tryResolveBeatmap();
                });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ipc.Beatmap.BindValueChanged(_ => onBeatmapChanged(), true);
            ipc.State.BindValueChanged(_ => applyDecision());
            ipc.IsConnected.BindValueChanged(c =>
            {
                if (!c.NewValue)
                {
                    // Disconnect path: stop music, drop the resolved beatmap.
                    hasResolvedBeatmap = false;
                    wantedBeatmapId = 0;
                }

                applyDecision();
            });
        }

        private void onBeatmapChanged()
        {
            int newId = ipc.Beatmap.Value?.OnlineID ?? 0;

            if (newId == wantedBeatmapId && hasResolvedBeatmap)
                return;

            wantedBeatmapId = newId;
            hasResolvedBeatmap = false;

            tryResolveBeatmap();
            applyDecision();
        }

        private void tryResolveBeatmap()
        {
            if (wantedBeatmapId <= 0)
                return;

            var localBeatmap = beatmapManager.QueryBeatmap(b => b.OnlineID == wantedBeatmapId);

            if (localBeatmap == null)
                return;

            var working = beatmapManager.GetWorkingBeatmap(localBeatmap);

            // Setting the global beatmap drives MusicController's track-change pipeline.
            globalBeatmap.Value = working;
            working.PrepareTrackForPreview(looping: true);

            hasResolvedBeatmap = true;
            Logger.Log($"[TournamentLobbyMusic] Resolved beatmap {wantedBeatmapId} for lobby playback");

            applyDecision();
        }

        private void applyDecision()
        {
            if (ShouldPlay(ipc.IsConnected.Value, ipc.State.Value, hasResolvedBeatmap))
            {
                music.EnsurePlayingSomething();
            }
            else
            {
                // Stop (not Pause) so MusicController doesn't auto-resume on screen
                // changes elsewhere — we own when music plays here.
                if (music.IsPlaying)
                    music.Stop();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();

            if (music.IsNotNull() && music.IsPlaying)
                music.Stop();
        }
    }
}
```

- [x] **Step 2: Verify it builds**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`

Expected: build succeeds.

- [x] **Step 3: Re-run the trigger-model unit tests**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~TournamentLobbyMusicTest`

Expected: 7 tests pass (the new wiring didn't break `ShouldPlay`).

- [x] **Step 4: Commit**

```bash
git add osu.Game.Tournament/Components/TournamentLobbyMusic.cs
git commit -m "wire TournamentLobbyMusic to MusicController and global beatmap"
```

---

## Task 3: Register in `TournamentGameBase` for multiplayer-spectator mode only

**Files:**
- Modify: `osu.Game.Tournament/TournamentGameBase.cs` around line 232 (the `Add(new MultiplayerIPCWriter())` line).

- [x] **Step 1: Add the new `using`**

Verify whether `using osu.Game.Tournament.Components;` is already present near the top of `TournamentGameBase.cs`. If not, add it.

Search: `grep -n "Tournament.Components" osu.Game.Tournament/TournamentGameBase.cs`

If absent, insert at the appropriate spot in the `using` block (sorted between `osu.Game.Tournament.IO` and `osu.Game.Tournament.IPC`).

- [x] **Step 2: Add the component next to `MultiplayerIPCWriter`**

In `osu.Game.Tournament/TournamentGameBase.cs`, find the existing block:

```csharp
                Add(ipc);

                if (ipc is MultiplayerMatchIPCInfo)
                    Add(new MultiplayerIPCWriter());
```

Change it to:

```csharp
                Add(ipc);

                if (ipc is MultiplayerMatchIPCInfo)
                {
                    Add(new MultiplayerIPCWriter());
                    Add(new TournamentLobbyMusic());
                }
```

- [x] **Step 3: Verify the project builds**

Run: `dotnet build osu.Game.Tournament/osu.Game.Tournament.csproj`

Expected: build succeeds with no warnings about the new code.

- [x] **Step 4: Run the full tournament test suite to verify nothing regressed**

Run: `dotnet test osu.Game.Tournament.Tests/osu.Game.Tournament.Tests.csproj --filter FullyQualifiedName~NonVisual`

Expected: all NonVisual tests pass (including the existing `MultiplayerIPCWriterTest` suite — the new component adds itself to the same multiplayer branch and shouldn't affect IPC writes).

- [x] **Step 5: Commit**

```bash
git add osu.Game.Tournament/TournamentGameBase.cs
git commit -m "register TournamentLobbyMusic in multiplayer spectator branch"
```

---

## Task 4: Manual smoke test

Automated audio playback testing is not practical (no real `WorkingBeatmap` track in headless tests). Verify behaviour manually before considering the work done.

**Files:** none.

- [ ] **Step 1: Run the tournament client**

```
dotnet run --project osu.Game.Tournament.Desktop
```

- [ ] **Step 2: Verify lobby music behavior**

In a tournament configured with `UseMultiplayerSpectating = true`:

1. Open the gameplay screen → connect to a multiplayer room with a known beatmap that's locally available.
2. **Expected:** within a few seconds of connecting, the room's map music starts playing on loop.
3. Have the host click Start.
4. **Expected:** state transitions to `WaitingForClients` — music continues. When `Playing` begins (gameplay frames arriving), music stops cleanly.
5. Let the round finish (or abort).
6. **Expected:** after the auto-advance back to the map pool (~14s after `Ranking`), lobby music resumes for the current map.
7. Change the playlist map in the room.
8. **Expected:** music swaps to the new beatmap.
9. Disconnect from the room.
10. **Expected:** music stops; doesn't auto-resume.

- [ ] **Step 3: Check the runtime log for the resolution message**

Run: `grep "TournamentLobbyMusic" tournaments/default/logs/runtime.log` (path depends on the storage layout — adjust to wherever the tournament storage lives).

Expected: one `Resolved beatmap <id>` line per unique map encountered.

- [ ] **Step 4: No commit; this task is verification only.**

---

## Self-review

**Spec coverage:**

- Trigger model (Idle/WaitingForClients vs Playing/Ranking) — Task 1 unit tests every enum case; `ShouldPlay` enforces it.
- Multiplayer-spectator-only registration — Task 3 wraps `Add(new TournamentLobbyMusic())` in the existing `ipc is MultiplayerMatchIPCInfo` branch.
- Global beatmap sync via `BeatmapManager.QueryBeatmap` → `GetWorkingBeatmap` → set `globalBeatmap.Value` — Task 2 `tryResolveBeatmap`.
- `PrepareTrackForPreview(looping: true)` — Task 2 `tryResolveBeatmap`.
- Don't fall back to random track when no beatmap — Task 2 gates `EnsurePlayingSomething` behind `hasResolvedBeatmap` via `ShouldPlay`.
- Realm subscription retry for late beatmap imports — Task 2 `realm.RegisterForNotifications` + `tryResolveBeatmap` re-attempt.
- Stop on disconnect — Task 2 `IsConnected` binding clears `hasResolvedBeatmap` and re-evaluates.
- Stop on dispose — Task 2 `Dispose` override.

**Placeholder scan:** none. Every code block is complete, every command has an expected outcome, no "fill in X" notes.

**Type consistency:** `ShouldPlay` signature is identical in Task 1 and Task 2. `wantedBeatmapId`/`hasResolvedBeatmap` field names match across all sites. `ipc.Beatmap`, `ipc.State`, `ipc.IsConnected` match the `MultiplayerMatchIPCInfo` API verified during brainstorming.
