﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    // ReSharper disable once PartialTypeWithSinglePart
    public partial class DrawableTeamWithPlayers : CompositeDrawable
    {
        public DrawableTeamWithPlayers(TournamentTeam? team, TeamColour colour)
        {
            AutoSizeAxes = Axes.Both;

            var players = team?.Players ?? new BindableList<TournamentUser>();

            // split the players into two even columns, favouring the first column if odd.
            int split = (int)Math.Ceiling(players.Count / 2f);

            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(30),
                    Children = new Drawable[]
                    {
                        new DrawableTeamTitleWithHeader(team, colour),
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Padding = new MarginPadding { Left = 10 },
                            Spacing = new Vector2(30),
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    Direction = FillDirection.Vertical,
                                    AutoSizeAxes = Axes.Both,
                                    Spacing = new Vector2(12),
                                    ChildrenEnumerable = players.Take(split).Select(createPlayerText),
                                },
                                new FillFlowContainer
                                {
                                    Direction = FillDirection.Vertical,
                                    AutoSizeAxes = Axes.Both,
                                    Spacing = new Vector2(12),
                                    ChildrenEnumerable = players.Skip(split).Select(createPlayerText),
                                },
                            }
                        },
                    }
                },
            };

            Drawable createPlayerText(TournamentUser p) =>
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(16),
                    Children = new Drawable[]
                    {
                        new UpdateableFlag(p.CountryCode)
                        {
                            Margin = new MarginPadding { Top = 3 },
                            Size = new Vector2(32, 23),
                        },
                        new TournamentSpriteText
                        {
                            Text = p.Username,
                            Font = OsuFont.Torus.With(size: 24, weight: FontWeight.SemiBold),
                            Colour = Color4.White,
                        },
                    }
                };
        }
    }
}
