// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Tournament.IO;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class DrawableTeamFlag : Container
    {
        private readonly TournamentTeam team;

        [UsedImplicitly]
        private Bindable<string> flag;

        // private PoCFlagContainer flagContainer;

        public DrawableTeamFlag(TournamentTeam team)
        {
            this.team = team;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures, TournamentVideoResourceStore storage)
        {
            if (team == null) return;

            if (Size[0] == 0)
            {
                Size = new Vector2(67, 67);
            }

            Masking = true;
            CornerRadius = 5;

            (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ =>
            {
                if (storage.GetStream(team.FlagName.Value) != null)
                {
                    Child = new TourneyVideo(team.FlagName.Value) { Loop = true, RelativeSizeAxes = Axes.Both };
                }
                else
                {
                    var newTexture = textures.Get($@"Flags/{flag}");

                    if (newTexture == null)
                    {
                        return;
                    }

                    var sprite = new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill
                    };
                    sprite.Texture = newTexture;
                    Child = sprite;
                }
            }, true);
        }
    }
}
