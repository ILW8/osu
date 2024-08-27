// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
            Logger.Log($"Received message: {Encoding.UTF8.GetString(message.Content.Span)}");
        }

        public void SaveBracket()
        {
            return;
        }

        public void UpdateScore(int scoreLeft, int scoreRight)
        {
            return;
        }
    }
}
