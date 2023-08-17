// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    public partial class TournamentBeatmapPanel : CompositeDrawable
    {
        public readonly TournamentBeatmap Beatmap;

        private readonly string mod;

        public const float HEIGHT = 50;
        public bool Tall;

        private readonly Bindable<TournamentMatch> currentMatch = new Bindable<TournamentMatch>();
        private Box flash;

        public TournamentBeatmapPanel(TournamentBeatmap beatmap, string mod = null, bool tall = false)
        {
            ArgumentNullException.ThrowIfNull(beatmap);

            Beatmap = beatmap;
            this.mod = mod;

            Width = 400;
            Height = HEIGHT;
            Tall = tall;
        }

        [BackgroundDependencyLoader]
        private void load(LadderInfo ladder)
        {
            currentMatch.BindValueChanged(matchChanged);
            currentMatch.BindTo(ladder.CurrentMatch);

            Masking = true;

            AddRangeInternal(new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                },
                new UpdateableOnlineBeatmapSetCover
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Gray(0.5f),
                    OnlineInfo = Beatmap,
                },
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Padding = new MarginPadding(8),
                    Direction = FillDirection.Vertical,
                    Children = Tall
                        ? new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Text = string.IsNullOrEmpty(Beatmap.Metadata.Title) ? "unknown artist" : Beatmap.Metadata.Title,
                                Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14),
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Margin = new MarginPadding { Top = -2.0f },
                                Children = new Drawable[]
                                {
                                    new TournamentSpriteText
                                    {
                                        Text = "by",
                                        Padding = new MarginPadding { Right = 4 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 12)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = string.IsNullOrEmpty(Beatmap.Metadata.Artist) ? "unknown artist" : Beatmap.Metadata.Artist,
                                        Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 12),
                                    },
                                }
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Children = new Drawable[]
                                {
                                    new TournamentSpriteText
                                    {
                                        Text = "mapper",
                                        Padding = new MarginPadding { Right = 4 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 11)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = Beatmap.Metadata.Author.Username,
                                        Padding = new MarginPadding { Right = 12 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 11)
                                    },
                                }
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Margin = new MarginPadding { Top = -2.0f },
                                Children = new Drawable[]
                                {
                                    new TournamentSpriteText
                                    {
                                        Text = "difficulty",
                                        Padding = new MarginPadding { Right = 4 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 12)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = Beatmap.DifficultyName,
                                        Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 12)
                                    },
                                }
                            }
                        }
                        : new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Text = Beatmap.GetDisplayTitleRomanisable(false, false),
                                Font = OsuFont.Torus.With(weight: FontWeight.Bold),
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Children = new Drawable[]
                                {
                                    new TournamentSpriteText
                                    {
                                        Text = "mapper",
                                        Padding = new MarginPadding { Right = 5 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = Beatmap.Metadata.Author.Username,
                                        Padding = new MarginPadding { Right = 20 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = "difficulty",
                                        Padding = new MarginPadding { Right = 5 },
                                        Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                    },
                                    new TournamentSpriteText
                                    {
                                        Text = Beatmap.DifficultyName,
                                        Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                    },
                                }
                            }
                        },
                },
                flash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Gray,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            });

            if (!string.IsNullOrEmpty(mod))
            {
                AddInternal(new TournamentModIcon(mod)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding(10),
                    Width = 60,
                    RelativeSizeAxes = Axes.Y,
                });
            }
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch> match)
        {
            if (match.OldValue != null)
                match.OldValue.PicksBans.CollectionChanged -= picksBansOnCollectionChanged;
            match.NewValue.PicksBans.CollectionChanged += picksBansOnCollectionChanged;
            updateState();
        }

        private void picksBansOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
            => updateState();

        private BeatmapChoice choice;

        private void updateState()
        {
            var found = currentMatch.Value.PicksBans.FirstOrDefault(p => p.BeatmapID == Beatmap.OnlineID);

            bool doFlash = found != choice;
            choice = found;

            if (found != null)
            {
                if (doFlash)
                    flash?.FadeOutFromOne(500).Loop(0, 10);

                BorderThickness = 6;

                BorderColour = TournamentGame.GetTeamColour(found.Team);

                switch (found.Type)
                {
                    case ChoiceType.Pick:
                        Colour = Color4.White;
                        Alpha = 1;
                        break;

                    case ChoiceType.Ban:
                        Colour = Color4.Gray;
                        Alpha = 0.5f;
                        break;
                }
            }
            else
            {
                Colour = Color4.White;
                BorderThickness = 0;
                Alpha = 1;
            }
        }
    }
}
