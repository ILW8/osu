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
