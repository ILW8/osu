// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class DrawableTeamFlag : Container
    {
        private readonly TournamentTeam? team;

        [UsedImplicitly]
        private Bindable<string>? flag;

        private Sprite? flagSprite;
        private Sprite? overlayFlag;

        public DrawableTeamFlag(TournamentTeam? team)
        {
            this.team = team;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            if (team == null) return;

            Size = new Vector2(75);

            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 5,
                    Child = flagSprite = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill
                    }
                },
                overlayFlag = new Sprite
                {
                    Size = new Vector2(75, 54),
                    Scale = new Vector2(0.6f),
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(-12, -8)
                }
            };

            (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ =>
            {
                flagSprite.Texture = textures.Get($@"Flags/{team.FlagName}");
                overlayFlag.Texture = textures.Get($@"Flags/AU"); // Or different texture path
            }, true);
        }
    }
}
