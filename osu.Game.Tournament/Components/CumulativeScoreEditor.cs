// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Control-panel widget for manually editing the current match's score totals and completion state.
    /// Only shown while cumulative scoring is enabled, where click-to-edit on the score displays is disabled.
    /// </summary>
    public partial class CumulativeScoreEditor : FillFlowContainer
    {
        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();
        private readonly Bindable<bool> useCumulativeScore = new Bindable<bool>();

        private SettingsNumberBox team1Score = null!;
        private SettingsNumberBox team2Score = null!;
        private LabelledSwitchButton completed = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 5f);

            Children = new Drawable[]
            {
                team1Score = new SettingsNumberBox { LabelText = "Team 1 score" },
                team2Score = new SettingsNumberBox { LabelText = "Team 2 score" },
                completed = new LabelledSwitchButton { Label = "Match completed" },
            };

            currentMatch.BindTo(ladder.CurrentMatch);
            useCumulativeScore.BindTo(ladder.CumulativeScore);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentMatch.BindValueChanged(matchChanged, true);
            useCumulativeScore.BindValueChanged(_ => updateVisibility(), true);
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            // Point each control at the current match's bindables (throwaway when no match is selected).
            team1Score.Current = match.NewValue?.Team1Score ?? new Bindable<int?>();
            team2Score.Current = match.NewValue?.Team2Score ?? new Bindable<int?>();
            completed.Current = match.NewValue?.Completed ?? new Bindable<bool>();

            updateVisibility();
        }

        // ponytail: off-screen operator panel, an Alpha toggle is enough — no relayout needed.
        private void updateVisibility()
            => Alpha = useCumulativeScore.Value && currentMatch.Value != null ? 1 : 0;
    }
}
