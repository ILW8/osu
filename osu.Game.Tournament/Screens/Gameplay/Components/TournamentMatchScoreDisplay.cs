// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    // TODO: Update to derive from osu-side class?
    public partial class TournamentMatchScoreDisplay : CompositeDrawable
    {
        [Resolved]
        protected LadderInfo LadderInfo { get; private set; }

        private const float bar_height = 18;

        private readonly BindableInt score1 = new BindableInt();
        private readonly BindableInt score2 = new BindableInt();
        private readonly BindableFloat accuracy1 = new BindableFloat();
        private readonly BindableFloat accuracy2 = new BindableFloat();
        private readonly BindableInt missCount1 = new BindableInt();
        private readonly BindableInt missCount2 = new BindableInt();

        private readonly Bindable<WinCondition> winCondition = new Bindable<WinCondition>();
        private readonly Bindable<TournamentBeatmap> currentBeatmap = new Bindable<TournamentBeatmap>();

        private readonly MatchScoreCounter score1Text;
        private readonly MatchScoreCounter score2Text;
        private readonly AccScoreCounter acc1Text;
        private readonly AccScoreCounter acc2Text;

        private readonly MatchScoreDiffCounter scoreDiffText;
        private readonly MatchAccDiffCounter accDiffText;

        private readonly Drawable score1Bar;
        private readonly Drawable score2Bar;

        public TournamentMatchScoreDisplay()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

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
                score1Text = new MatchScoreCounter
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Alpha = 0
                },
                acc1Text = new AccScoreCounter
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    // Y = -128
                },
                acc2Text = new AccScoreCounter
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
                score2Text = new MatchScoreCounter
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Alpha = 0
                },
                scoreDiffText = new MatchScoreDiffCounter
                {
                    Anchor = Anchor.TopCentre,
                    Margin = new MarginPadding
                    {
                        Top = bar_height / 4,
                        Horizontal = 8
                    },
                    Alpha = 0
                },
                accDiffText = new MatchAccDiffCounter
                {
                    Anchor = Anchor.TopCentre,
                    Margin = new MarginPadding
                    {
                        Top = bar_height / 4,
                        Horizontal = 8
                    },
                    Alpha = 0
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            score1.BindValueChanged(_ => updateScores());
            // score1Mult.BindValueChanged(_ => updateScores());
            score1.BindTo(ipc.Score1);

            score2.BindValueChanged(_ => updateScores());
            score2.BindTo(ipc.Score2);

            accuracy1.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            accuracy1.BindTo(ipc.Accuracy1);
            accuracy2.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            accuracy2.BindTo(ipc.Accuracy2);

            missCount1.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            missCount1.BindTo(ipc.MissCount1);
            missCount2.BindValueChanged(_ => Scheduler.AddOnce(updateScores));
            missCount2.BindTo(ipc.MissCount2);

            currentBeatmap.BindTo(ipc.Beatmap);
            currentBeatmap.BindValueChanged(_ => Scheduler.AddOnce(updateWinCondition));
        }

        private void updateWinCondition()
        {
            var activeBeatmap = LadderInfo.CurrentMatch.Value?.Round.Value?.Beatmaps.FirstOrDefault(b => b.Beatmap?.OnlineID == currentBeatmap.Value?.OnlineID);

            if (activeBeatmap is null) return;

            if (winCondition.Value == activeBeatmap.WinCondition.Value) return;

            Logger.Log($"Found matching beatmap in mappool, win condition updated: {activeBeatmap.WinCondition}", LoggingTarget.Runtime, LogLevel.Important);
            winCondition.Value = activeBeatmap.WinCondition.Value;

            (winCondition.Value == WinCondition.Accuracy ? score1Text : acc1Text).FadeOut(250);
            (winCondition.Value == WinCondition.Accuracy ? score2Text : acc2Text).FadeOut(250);
            (winCondition.Value == WinCondition.Accuracy ? scoreDiffText : accDiffText).FadeOut(250);

            (winCondition.Value == WinCondition.Accuracy ? acc1Text : score1Text).Delay(350).FadeIn(250);
            (winCondition.Value == WinCondition.Accuracy ? acc2Text : score2Text).Delay(350).FadeIn(250);
            (winCondition.Value == WinCondition.Accuracy ? accDiffText : scoreDiffText).Delay(350).FadeIn(250);

            Scheduler.AddOnce(updateScores);
        }

        private void updateScores()
        {
            score1Text.Current.Value = score1.Value;
            score2Text.Current.Value = score2.Value;
            acc1Text.Current.Value = accuracy1.Value;
            acc2Text.Current.Value = accuracy2.Value;

            float accDiff = Math.Max(accuracy1.Value, accuracy2.Value) - Math.Min(accuracy1.Value, accuracy2.Value);

            // todo: set winner width for acc based on 100s/50s count instead of numeric accuracy
            float fullWinnerWidth = winCondition.Value == WinCondition.Accuracy
                ? Math.Min(0.4f, MathF.Pow(accDiff / 8f, 0.7f) / 2)
                : Math.Min(0.4f, MathF.Pow(Math.Abs(score1.Value - score2.Value) / 32f, 0.75f) / 2);

            bool winnerSide = winCondition.Value == WinCondition.Accuracy ? accuracy1.Value > accuracy2.Value : score1.Value <= score2.Value;

            var winningAccText = winnerSide ? acc1Text : acc2Text;
            var winningMissText = winnerSide ? score1Text : score2Text;
            var losingAccText = !winnerSide ? acc1Text : acc2Text;
            var losingMissText = !winnerSide ? score1Text : score2Text;
            var winningBarBase = winnerSide ? score1Bar : score2Bar;
            var losingBarBase = !winnerSide ? score1Bar : score2Bar;

            winningAccText.Winning = true;
            winningMissText.Winning = true;

            // mark both as winning if same accuracy/miss count
            losingAccText.Winning = Math.Abs(accuracy1.Value - accuracy2.Value) < 0.005;
            losingMissText.Winning = score1.Value == score2.Value;

            losingBarBase.ResizeWidthTo(0, 400, Easing.OutQuint);
            winningBarBase.ResizeWidthTo(fullWinnerWidth, 400, Easing.OutQuint);

            int diff = Math.Abs(score1.Value - score2.Value);
            scoreDiffText.Alpha = diff != 0 ? 1 : 0;
            scoreDiffText.Current.Value = -diff;
            scoreDiffText.Origin = score1.Value > score2.Value ? Anchor.TopLeft : Anchor.TopRight;

            accDiffText.Alpha = Math.Abs(accuracy1.Value - accuracy2.Value) < 0.01 ? 0 : 1; // 0.01 tolerance as osu! reports acc down to 2 decimal places
            accDiffText.Current.Value = -Math.Abs(accuracy1.Value - accuracy2.Value);
            accDiffText.Origin = accuracy1.Value > accuracy2.Value ? Anchor.TopLeft : Anchor.TopRight;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            score1Text.X = -Math.Max(5 + score1Text.DrawWidth / 2, score1Bar.DrawWidth);
            acc1Text.X = -Math.Max(5 + acc1Text.DrawWidth / 2, score1Bar.DrawWidth);

            score2Text.X = Math.Max(5 + score2Text.DrawWidth / 2, score2Bar.DrawWidth);
            acc2Text.X = Math.Max(5 + acc2Text.DrawWidth / 2, score2Bar.DrawWidth);
        }

        private partial class AccScoreCounter : MatchScoreCounter
        {
            protected override double RollingDuration => 500;

            protected override LocalisableString FormatCount(double count) => $"{count:F2} %";
        }

        private partial class MatchScoreCounter : CommaSeparatedScoreCounter
        {
            protected override double RollingDuration => 200;
            private OsuSpriteText displayedSpriteText;

            public MatchScoreCounter()
            {
                Margin = new MarginPadding { Top = bar_height, Horizontal = 10 };
            }

            public bool Winning
            {
                set => updateFont(value);
            }

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                displayedSpriteText = s;
                displayedSpriteText.Spacing = new Vector2(-6);
                updateFont(false);
            });

            private void updateFont(bool winning)
                => displayedSpriteText.Font = winning
                    ? OsuFont.Torus.With(weight: FontWeight.Bold, size: 50, fixedWidth: true)
                    : OsuFont.Torus.With(weight: FontWeight.Regular, size: 40, fixedWidth: true);
        }

        private partial class MatchScoreDiffCounter : CommaSeparatedScoreCounter
        {
            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                s.Spacing = new Vector2(-2);
                s.Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: bar_height, fixedWidth: true);
            });
        }

        private partial class MatchAccDiffCounter : MatchScoreDiffCounter
        {
            protected override double RollingDuration => 500;

            protected override LocalisableString FormatCount(double count) => $"{count:F2} %";
        }
    }
}
