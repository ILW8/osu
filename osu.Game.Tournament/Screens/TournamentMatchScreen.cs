// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens
{
    public abstract partial class TournamentMatchScreen : TournamentScreen
    {
        protected readonly Bindable<TournamentMatch?> CurrentMatch = new Bindable<TournamentMatch?>();
        private WarningBox? noMatchWarning;

        [Resolved(CanBeNull = true)]
        private ITournamentWsControl? websocketController { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            CurrentMatch.BindTo(LadderInfo.CurrentMatch);
            CurrentMatch.BindValueChanged(CurrentMatchChanged, true);
        }

        protected virtual void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            if (match.NewValue == null)
            {
                AddInternal(noMatchWarning = new WarningBox("Choose a match first from the brackets screen"));
                return;
            }

            noMatchWarning?.Expire();
            noMatchWarning = null;

            UpdatePoolState();
        }

        protected void UpdatePoolState()
        {
            Logger.Log("UPDATE POOL STATE!!!!!");

            if (CurrentMatch.Value?.Round.Value == null)
            {
                // websocketController?.BroadcastMappoolChange(new Dictionary<string, int>());
                return;
            }

            var modIndices = new Dictionary<string, int>();
            var poolState = CurrentMatch.Value.Round.Value.Beatmaps
                                        .Select(b =>
                                        {
                                            if (!modIndices.TryGetValue(b.Mods, out int value))
                                                modIndices[b.Mods] = 1;
                                            else
                                                modIndices[b.Mods] = ++value;

                                            return new { b.Mods, OnlineID = b.Beatmap?.OnlineID ?? 0, Index = modIndices[b.Mods] };
                                        })
                                        .ToDictionary(b => $"{b.Mods}{b.Index}",
                                            b => new Dictionary<string, int?>
                                            {
                                                {
                                                    "Team", CurrentMatch.Value.PicksBans.Where(pb => pb.BeatmapID == b.OnlineID)
                                                                        .Select(pb => pb.Team == TeamColour.Red ? 0 : 1)
                                                                        .Cast<int?>()
                                                                        .FirstOrDefault()
                                                },
                                                {
                                                    "Banned",
                                                    CurrentMatch.Value.PicksBans.Where(pb => pb.BeatmapID == b.OnlineID)
                                                                .Select(pb => pb.Type == ChoiceType.Pick ? 0 : 1)
                                                                .Cast<int?>()
                                                                .FirstOrDefault()
                                                }
                                            });

            websocketController?.BroadcastMappoolChange(poolState);
        }
    }
}
