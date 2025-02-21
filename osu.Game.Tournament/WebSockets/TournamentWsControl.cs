// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Logging;
using osu.Game.IPC;
using osuTK;
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

            // bracket <verb> <argument>
            // verb: move, zoom
            // arg: reset, (x,y) for move, 0.0f scale adjust factor for zoom
            if (cmd.StartsWith("bracket ", StringComparison.Ordinal))
            {
                string[] bracketCmds = cmd.Split(" ", 3);
                Logger.Log($"got bracket command: {string.Join(',', bracketCmds)}");

                if (bracketCmds.Length == 3)
                {
                    string verb = bracketCmds[1];
                    string arg = bracketCmds[2];

                    Logger.Log($"verb: {verb}, arg: {arg}");

                    // todo: fix this messy nesting
                    switch (verb)
                    {
                        case "move":
                            Logger.Log("move");

                            if (arg.Equals("reset", StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Log("move reset");

                                Schedule(() => OnBracketTranslateChangeRequested?.Invoke(ITournamentWsControl.BracketViewTransformMode.Absolute, new Vector2(0, 0)));
                            }
                            else
                            {
                                string[] coords = arg.Split(',');

                                if (coords.Length == 2)
                                {
                                    if (float.TryParse(coords[0], out float x) && float.TryParse(coords[1], out float y))
                                    {
                                        Schedule(() => OnBracketTranslateChangeRequested?.Invoke(ITournamentWsControl.BracketViewTransformMode.Absolute, new Vector2(x, y)));
                                    }
                                }
                            }

                            break;

                        case "translate":
                            string[] translateDelta = arg.Split(',');

                            if (translateDelta.Length == 2)
                            {
                                if (float.TryParse(translateDelta[0], out float x) && float.TryParse(translateDelta[1], out float y))
                                {
                                    Schedule(() => OnBracketTranslateChangeRequested?.Invoke(ITournamentWsControl.BracketViewTransformMode.Relative, new Vector2(x, y)));
                                }
                            }

                            break;

                        case "zoom":
                            if (arg.Equals("reset", StringComparison.OrdinalIgnoreCase))
                            {
                                Schedule(() => OnBracketZoomChangeRequested?.Invoke(ITournamentWsControl.BracketViewTransformMode.Absolute, 1.0f));
                            }
                            else
                            {
                                if (float.TryParse(arg, out float zoomChange))
                                {
                                    Schedule(() => OnBracketZoomChangeRequested?.Invoke(ITournamentWsControl.BracketViewTransformMode.Relative, zoomChange));
                                }
                            }

                            break;

                        default:
                            Logger.Log($"unknown bracket command verb: {verb}");
                            break;
                    }
                }
            }

            // `pickban` sets the pickban mode
            // `dopickban` performs the pick/ban action against a slot

            if (cmd.StartsWith("pickban ", StringComparison.Ordinal))
            {
                string[] parts = cmd.Split(" ");

                if (parts.Length != 3)
                    return;

                if (int.TryParse(parts[2], out int isPick))
                    OnPickBanActionUpdate?.Invoke(parts[1], isPick);
            }

            if (cmd.StartsWith("dopickban ", StringComparison.Ordinal))
            {
                var match = pickBanModSlotRegex().Match(cmd);

                if (match.Success)
                {
                    var mod = match.Groups[1];
                    var index = match.Groups[2];
                    OnPerformPickBanRequested?.Invoke(mod.Value, int.Parse(index.Value));
                }
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

        public void BroadcastMappoolChange(Dictionary<string, Dictionary<string, int?>> poolSize) => Broadcast(System.Text.Json.JsonSerializer.Serialize(new { sizes = poolSize }));

        public event Action? OnSaveRequested;
        public event Action<int, int>? OnTeamScoreUpdateRequested;
        public event Action<string, int>? OnPickBanActionUpdate;
        public event Action<string, int>? OnPerformPickBanRequested;
        public event Action<Key>? OnSceneChangeRequested;
        public event Action? OnWarmupToggleRequested;

        public event Action<ITournamentWsControl.BracketViewTransformMode, float>? OnBracketZoomChangeRequested;
        public event Action<ITournamentWsControl.BracketViewTransformMode, Vector2>? OnBracketTranslateChangeRequested;

        [GeneratedRegex(@"dopickban (-?\w{2})(\d+)")]
        private static partial Regex pickBanModSlotRegex();
    }
}
