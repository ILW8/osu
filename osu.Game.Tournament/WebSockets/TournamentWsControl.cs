// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Logging;
using osu.Game.IPC;

namespace osu.Game.Tournament.WebSockets
{
    public partial class TournamentWsControl : WebSocketServer, ITournamentWsControl
    {
        public override string Endpoint => @"tournament";

        [BackgroundDependencyLoader]
        private void load()
        {
            Task.Run(Start);
        }

        protected override void OnConnectionReady(WebSocketConnection connection)
        {
            Logger.Log($"connection ready {nameof(TournamentWsControl)}", LoggingTarget.Network, LogLevel.Debug);
        }

        protected override void OnConnectionMessage(WebSocketConnection connection, Message message)
        {
            string cmd = Encoding.UTF8.GetString(message.Content.Span).Trim();
            Logger.Log($"Received message: {cmd}");

            switch (cmd)
            {
                case "save":
                    Schedule(() => OnSaveRequested?.Invoke());
                    break;

                case "red add 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(0, 1));
                    break;

                case "blue add 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(1, 1));
                    break;

                case "red sub 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(0, -1));
                    break;

                case "blue sub 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(1, -1));
                    break;
            }
        }

        public event Action? OnSaveRequested;
        public event Action<int, int>? OnTeamScoreUpdateRequested;
    }
}
