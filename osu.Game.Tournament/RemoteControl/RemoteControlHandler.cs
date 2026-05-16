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
        public enum InviteResult
        {
            Accepted,
            NotAvailable, // running with FileBasedIPC — multiplayer endpoint unavailable
            NoInvite,     // no pending invite to act on
        }

        public class Callbacks
        {
            /// <summary>
            /// Switch the active screen. Return false from the lambda if the type is unknown
            /// to the scene manager; the handler will then return 400. The callback runs on
            /// the framework update thread.
            /// </summary>
            public Func<Type, Task<bool>> SwitchScreen { get; init; } = _ => Task.FromResult(false);

            /// <summary>
            /// Increment the current match score for the given side ("red" or "blue").
            /// Returns the new score, or null if there is no current match.
            /// </summary>
            public Func<string, Task<int?>> IncrementMatchScore { get; init; } = _ => Task.FromResult<int?>(null);

            /// <summary>
            /// Accept the current pending invite. Returns NotAvailable when multiplayer IPC
            /// isn't in use, NoInvite when there's nothing pending, otherwise Accepted.
            /// </summary>
            public Func<Task<InviteResult>> AcceptPendingInvite { get; init; } = () => Task.FromResult(InviteResult.NotAvailable);

            /// <summary>
            /// Dismiss the current pending invite. Same result semantics as <see cref="AcceptPendingInvite"/>.
            /// </summary>
            public Func<Task<InviteResult>> DismissPendingInvite { get; init; } = () => Task.FromResult(InviteResult.NotAvailable);
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
