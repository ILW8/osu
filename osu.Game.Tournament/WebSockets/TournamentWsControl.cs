// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Game.IPC;
using osuTK.Input;

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

        protected override void OnConnectionMessage(WebSocketConnection connection, Message message)
        {
            string cmd = Encoding.UTF8.GetString(message.Content.Span).Trim();

            if (cmd.StartsWith("scene", StringComparison.Ordinal) && cmd.Contains(' ', StringComparison.Ordinal))
            {
                if (Enum.TryParse(cmd.Split(" ", 2)[1].ToUpperInvariant(), out Key key))
                    OnSceneChangeRequested?.Invoke(key);

                return;
            }

            switch (cmd)
            {
                case "save":
                    Schedule(() => OnSaveRequested?.Invoke());
                    break;

                case "red add 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(1, 0));
                    break;

                case "blue add 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(0, 1));
                    break;

                case "red sub 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(-1, 0));
                    break;

                case "blue sub 1":
                    Schedule(() => OnTeamScoreUpdateRequested?.Invoke(0, -1));
                    break;

                case "toggle warmup":
                    Schedule(() => OnWarmupToggleRequested?.Invoke());
                    break;
            }
        }

        public event Action? OnSaveRequested;
        public event Action<int, int>? OnTeamScoreUpdateRequested;
        public event Action<Key>? OnSceneChangeRequested;
        public event Action? OnWarmupToggleRequested;
    }
}
