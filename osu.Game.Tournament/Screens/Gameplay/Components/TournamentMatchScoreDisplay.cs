// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Screens.Play.HUD;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class TournamentMatchScoreDisplay : MatchScoreDisplay
    {
        private const float bar_height = 18;

        private readonly BindableFloat accuracy1 = new BindableFloat();
        private readonly BindableFloat accuracy2 = new BindableFloat();

        private readonly Bindable<TournamentBeatmap> currentBeatmap = new Bindable<TournamentBeatmap>();

        private Drawable score1Bar = null!;
        private Drawable score2Bar = null!;

        public TournamentMatchScoreDisplay()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            accuracy1.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            accuracy1.BindTo(ipc.Accuracy1);
            accuracy2.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            accuracy2.BindTo(ipc.Accuracy2);

            currentBeatmap.BindTo(ipc.Beatmap!);

            InternalChildren = new[]
            {
                new Box
                {
                    Name = "top bar red (static)",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height / 4,
                    Width = 0.5f,
                    Colour = TournamentGame.COLOUR_RED,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopRight
                },
                new Box
                {
                    Name = "top bar blue (static)",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height / 4,
                    Width = 0.5f,
                    Colour = TournamentGame.COLOUR_BLUE,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopLeft
                },
                score1Bar = new Box
                {
                    Name = "top bar red",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height,
                    Width = 0,
                    Colour = TournamentGame.COLOUR_RED,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopRight
                },
                Score1Text = new AccScoreCounter
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    // Y = -128
                },
                Score2Text = new AccScoreCounter
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    // Y = -128
                },
                score2Bar = new Box
                {
                    Name = "top bar blue",
                    RelativeSizeAxes = Axes.X,
                    Height = bar_height,
                    Width = 0,
                    Colour = TournamentGame.COLOUR_BLUE,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopLeft
                },
            };
        }

        private void updateScores()
        {
            Score1Text.Current.Value = accuracy1.Value;
            Score2Text.Current.Value = accuracy2.Value;
            float accDiff = Math.Max(accuracy1.Value, accuracy2.Value) - Math.Min(accuracy1.Value, accuracy2.Value);

            float fullWinnerWidth = Math.Min(0.4f, MathF.Pow(accDiff / 8f, 0.7f) / 2);

            Logger.Log($"acc1: {accuracy1.Value} | acc2: {accuracy2.Value}", LoggingTarget.Runtime, LogLevel.Important);

            bool winnerSide = accuracy1.Value > accuracy2.Value;

            var winningAccText = winnerSide ? Score1Text : Score2Text;
            var losingAccText = !winnerSide ? Score1Text : Score2Text;
            var winningBarBase = winnerSide ? score1Bar : score2Bar;
            var losingBarBase = !winnerSide ? score1Bar : score2Bar;

            winningAccText.Winning = true;
            // mark both as winning if same accuracy/miss count
            losingAccText.Winning = Math.Abs(accuracy1.Value - accuracy2.Value) < 0.005;

            losingBarBase.ResizeWidthTo(0, 400, Easing.OutQuint);
            winningBarBase.ResizeWidthTo(fullWinnerWidth, 400, Easing.OutQuint);
        }

        private partial class AccScoreCounter : MatchScoreCounter
        {
            protected override double RollingDuration => 500;

            protected override LocalisableString FormatCount(double count) => $"{count:F2} %";
        }
    }
}
