// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Ladder.Components;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Visual representation of a <see cref="MatchSet"/>: 2 (or 3 if tiebreaker) rows showing
    /// each map's slot name and the cumulative red/blue scores. The outer border tints to the
    /// leading team once both maps in the set have scores.
    /// </summary>
    public partial class TournamentSetPanel : CompositeDrawable
    {
        public const float HEIGHT = 50;
        public const float WIDTH = 400;

        public MatchSet Model { get; }

        private TeamColour? winnerColour;

        public TeamColour? Winner
        {
            get => winnerColour;
            set
            {
                winnerColour = value;
                updateWinState();
            }
        }

        private SetMapResultDisplay map1ResultDisplay = null!;
        private SetMapResultDisplay map2ResultDisplay = null!;
        private SetMapResultDisplay? map3ResultDisplay;
        private FillFlowContainer mainFlow = null!;

        public TournamentSetPanel(MatchSet set)
        {
            Model = set;

            Width = WIDTH;
            Height = HEIGHT;
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddRangeInternal(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.35f,
                },
                mainFlow = new FillFlowContainer
                {
                    Direction = FillDirection.Vertical,
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        map1ResultDisplay = new SetMapResultDisplay
                        {
                            RelativeSizeAxes = Axes.X,
                        },
                        map2ResultDisplay = new SetMapResultDisplay
                        {
                            RelativeSizeAxes = Axes.X,
                        },
                    }
                },
            });

            if (Model.IsTiebreaker)
            {
                mainFlow.Add(map3ResultDisplay = new SetMapResultDisplay
                {
                    RelativeSizeAxes = Axes.X,
                });
            }

            Model.Map1Id.BindValueChanged(vce => map1ResultDisplay.MapID = vce.NewValue, true);
            Model.Map2Id.BindValueChanged(vce => map2ResultDisplay.MapID = vce.NewValue, true);
            Model.Map3Id.BindValueChanged(vce =>
            {
                if (map3ResultDisplay != null)
                    map3ResultDisplay.MapID = vce.NewValue;
            }, true);

            map1ResultDisplay.ResultChanged += checkWinState;
            map2ResultDisplay.ResultChanged += checkWinState;
            if (map3ResultDisplay != null)
                map3ResultDisplay.ResultChanged += checkWinState;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            // BDL might not have run yet if disposed early.
            if (map1ResultDisplay != null)
                map1ResultDisplay.ResultChanged -= checkWinState;
            if (map2ResultDisplay != null)
                map2ResultDisplay.ResultChanged -= checkWinState;
            if (map3ResultDisplay != null)
                map3ResultDisplay.ResultChanged -= checkWinState;
        }

        private void checkWinState()
        {
            if (map1ResultDisplay.ScoreRed.Value == null
                || map2ResultDisplay.ScoreRed.Value == null
                || (Model.IsTiebreaker && map3ResultDisplay?.ScoreRed.Value == null))
            {
                Winner = null;
                return;
            }

            long redScore = (map1ResultDisplay.ScoreRed.Value ?? 0)
                            + (map2ResultDisplay.ScoreRed.Value ?? 0)
                            + (map3ResultDisplay?.ScoreRed.Value ?? 0);
            long blueScore = (map1ResultDisplay.ScoreBlue.Value ?? 0)
                             + (map2ResultDisplay.ScoreBlue.Value ?? 0)
                             + (map3ResultDisplay?.ScoreBlue.Value ?? 0);

            Winner = redScore < blueScore ? TeamColour.Blue : TeamColour.Red;
        }

        private void updateWinState()
        {
            if (winnerColour != null)
            {
                BorderThickness = 6;
                BorderColour = TournamentGame.GetTeamColour((TeamColour)winnerColour);
            }
            else
            {
                BorderThickness = 0;
            }
        }

        public partial class SetMapResultDisplay : CompositeDrawable
        {
            public event Action? ResultChanged;

            public readonly Bindable<long?> ScoreRed = new Bindable<long?>();
            public readonly Bindable<long?> ScoreBlue = new Bindable<long?>();

            private TournamentSpriteText slotText = null!;
            private long mapId;

            public long MapID
            {
                get => mapId;
                set
                {
                    mapId = value;
                    updateSlotName();
                }
            }

            private SetMapScoreCounter scoreCounterRed = null!;
            private SetMapScoreCounter scoreCounterBlue = null!;

            [Resolved]
            protected LadderInfo LadderInfo { get; private set; } = null!;

            protected readonly Bindable<TournamentMatch?> CurrentMatch = new Bindable<TournamentMatch?>();

            public SetMapResultDisplay()
            {
                Height = 16;
                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Container
                    {
                        Width = 0.3f,
                        RelativeSizeAxes = Axes.Both,
                        Child = slotText = new TournamentSpriteText
                        {
                            Origin = Anchor.Centre,
                            Anchor = Anchor.Centre,
                            Text = string.Empty,
                            Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14),
                        },
                    },
                    new Container
                    {
                        Masking = true,
                        Width = 0.35f,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.X,
                        X = 0.3f,
                        Child = scoreCounterRed = new SetMapScoreCounter
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        }
                    },
                    new Container
                    {
                        Masking = true,
                        Width = 0.35f,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.Both,
                        RelativePositionAxes = Axes.X,
                        X = 0.65f,
                        Child = scoreCounterBlue = new SetMapScoreCounter
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        }
                    }
                };

                ScoreRed.BindValueChanged(val => updateScore(scoreCounterRed, val.NewValue), true);
                ScoreBlue.BindValueChanged(val => updateScore(scoreCounterBlue, val.NewValue), true);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                CurrentMatch.BindTo(LadderInfo.CurrentMatch);
                CurrentMatch.BindValueChanged(_ => updateSlotName(), true);
            }

            private void updateSlotName()
            {
                if (CurrentMatch.Value == null || slotText == null)
                    return;

                if (mapId == 0)
                {
                    slotText.Text = string.Empty;
                    ScoreRed.Value = null;
                    ScoreBlue.Value = null;
                    ResultChanged?.Invoke();
                    return;
                }

                var poolMap = CurrentMatch.Value.Round.Value?.Beatmaps.FirstOrDefault(bm => bm.ID == mapId);
                slotText.Text = poolMap?.SlotName ?? "??";

                CurrentMatch.Value.MapScores.BindCollectionChanged((_, _) => refreshScores(), true);
            }

            private void refreshScores()
            {
                if (CurrentMatch.Value == null || slotText == null) return;

                string key = slotText.Text.ToString();

                if (CurrentMatch.Value.MapScores.TryGetValue(key, out var value))
                {
                    ScoreRed.Value = value.Item1;
                    ScoreBlue.Value = value.Item2;
                    ResultChanged?.Invoke();
                    return;
                }

                if (ScoreRed.Value == null) return;

                ScoreRed.Value = null;
                ScoreBlue.Value = null;
                ResultChanged?.Invoke();
            }

            private static void updateScore(SetMapScoreCounter counter, long? value)
            {
                switch (value)
                {
                    case null:
                    case 0:
                        counter.Current.Value = 0;
                        counter.DisplayedSpriteText?.Hide();
                        break;

                    default:
                        counter.Current.Value = 0;
                        counter.DisplayedSpriteText?.Show();
                        counter.Current.Value = (double)value;
                        break;
                }
            }
        }

        private partial class SetMapScoreCounter : CommaSeparatedScoreCounter
        {
            public OsuSpriteText? DisplayedSpriteText;

            protected override double RollingDuration => 350;

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                DisplayedSpriteText = s;
                DisplayedSpriteText.Font = OsuFont.Torus.With(size: 14);
            });
        }
    }
}
