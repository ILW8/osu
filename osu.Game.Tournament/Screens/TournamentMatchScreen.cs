// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens
{
    public abstract partial class TournamentMatchScreen : TournamentScreen
    {
        protected readonly Bindable<TournamentMatch?> CurrentMatch = new Bindable<TournamentMatch?>();
        private WarningBox? noMatchWarning;

        [Resolved]
        private ITournamentWsControl websocketController { get; set; } = null!;

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

            if (CurrentMatch.Value == null)
                return;

            if (CurrentMatch.Value.Round.Value == null)
            {
                websocketController.BroadcastMappoolChange(new Dictionary<string, int>());
                return;
            }

            var modsCount = CurrentMatch.Value.Round.Value.Beatmaps.GroupBy(b => b.Mods)
                                        .ToDictionary(g => g.Key, g => g.Count());

            websocketController.BroadcastMappoolChange(modsCount);
        }
    }
}
