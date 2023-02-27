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
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class DrawableTeamFlag : Container
    {
        private readonly TournamentTeam team;

        [UsedImplicitly]
        private Bindable<string> flag;

        private Sprite flagSprite;
        // private PoCFlagContainer flagContainer;

        public DrawableTeamFlag(TournamentTeam team)
        {
            this.team = team;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            if (team == null) return;

            if (Size[0] == 0)
            {
                Size = new Vector2(67, 67);
            }

            Masking = true;
            CornerRadius = 5;
            flagSprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill
            };

            // Child = new TourneyVideo("defaultAnimatedPfp", true)
            // {
            //     Loop = true,
            //     RelativeSizeAxes = Axes.Both,
            // };

            // (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ => flagSprite.Texture = textures.Get($@"Flags/{team.FlagName}"), true);
            (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ =>
            {
                Texture newTexture = textures.Get($@"Flags/{flag}");

                if (newTexture != null)
                {
                    Child = flagSprite;
                    flagSprite.Texture = newTexture;
                }
                else
                {
                    Child = new TourneyVideo(team.FlagName.Value, true) { Loop = true, RelativeSizeAxes = Axes.Both };
                }
            }, true);

            // Child = flagContainer = new FlagContainer();

            // (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ => flagContainer.Flag = team.FlagName.Value, true);
        }
    }

    public partial class PoCFlagContainer : Container
    {
        private string thisflag;
        // private Sprite thissprite;
        private TourneyVideo tourneyVideo;


        public PoCFlagContainer(string flag)
        {
            thisflag = flag;
            // InternalChild = thissprite = new Sprite
            // {
            //     RelativeSizeAxes = Axes.Both,
            //     Anchor = Anchor.Centre,
            //     Origin = Anchor.Centre,
            //     FillMode = FillMode.Fill
            // };
            Child = tourneyVideo = new TourneyVideo("bu", true)
            {
                Loop = true,
                RelativeSizeAxes = Axes.Both,
            };
        }

        // [BackgroundDependencyLoader]
        // private void load(TextureStore textures)
        // {
        //     thissprite.Texture = textures.Get($@"Flags/{thisflag}");
        // }
    }

    internal partial class FlagContainer : Container
    {
        private string flag;
        private TextureStore textureStore;
        private Sprite flagSprite;
        private TourneyVideo tourneyVideo;

        public string Flag
        {
            get => flag;
            set
            {
                flag = value;

                // if (flag == "abvsafas")
                // {
                //     flag = "351";
                // }

                Texture a = textureStore.Get($@"Flags/{flag}");


                    flagSprite.Texture = a;
                    // Child = flagSprite;

                // else
                // {
                //     Child = tourneyVideo = new TourneyVideo(flag);
                // }
            }
        }

        /*
         *     public virtual Texture Texture
    {
      get => this.texture;
      set
      {
        if (value == this.texture)
          return;
        this.texture?.Dispose();
        this.texture = value;
        float num1;
        if ((this.TextureRelativeSizeAxes & Axes.X) > Axes.None)
        {
          Texture texture = this.texture;
          num1 = (texture != null ? (float) texture.Width : 1f) / this.TextureRectangle.Width;
        }
        else
          num1 = this.TextureRectangle.Width;
        float num2;
        if ((this.TextureRelativeSizeAxes & Axes.Y) > Axes.None)
        {
          Texture texture = this.texture;
          num2 = (texture != null ? (float) texture.Height : 1f) / this.TextureRectangle.Height;
        }
        else
          num2 = this.TextureRectangle.Height;
        this.FillAspectRatio = num1 / num2;
        this.Invalidate(Invalidation.DrawNode);
        this.conservativeScreenSpaceDrawQuadBacking.Invalidate();
        if (!(this.Size == Vector2.Zero))
          return;
        Texture texture1 = this.texture;
        double x = texture1 != null ? (double) texture1.DisplayWidth : 0.0;
        Texture texture2 = this.texture;
        double y = texture2 != null ? (double) texture2.DisplayHeight : 0.0;
        this.Size = new Vector2((float) x, (float) y);
      }
    }
         */

        // public FlagContainer()
        // {
        //
        // }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            textureStore = textures;

            flagSprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill
            };

            tourneyVideo = new TourneyVideo("defaultAnimatedPfp", true)
            {
                Loop = true,
                RelativeSizeAxes = Axes.Both,
            };
            Child = flagSprite;
        }
    }
}
