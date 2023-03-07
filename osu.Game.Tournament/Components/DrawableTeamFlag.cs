// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Humanizer;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Video;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Timing;
using osu.Game.Skinning;
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

        private TextureStore textureStore;
        private TournamentFlagVideoResourceStore flagVideoStore;
        private GameHost gameHost;

        public DrawableTeamFlag(TournamentTeam team)
        {
            this.team = team;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures, TournamentFlagVideoResourceStore flagVideoStore, GameHost gameHost)
        {
            // Logger.Log(@"Called DrawableTeamFlag.load", LoggingTarget.Information);
            this.textureStore = textures;
            this.flagVideoStore = flagVideoStore;
            this.gameHost = gameHost;

            if (team == null) return;

            if (Size[0] == 0)
            {
                Size = new Vector2(67, 67);
            }

            Masking = false;
            CornerRadius = 5;

            // (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ => InternalChild = GetFlag(Size), true);
            (flag = team.FlagName.GetBoundCopy()).BindValueChanged(_ =>
            {
                var stream = flagVideoStore.GetStream(flag.Value);

                if (stream == null)
                {
                    InternalChild = GetFlag(Size);
                    return;
                }

                InternalChild = new Video(stream, false) // this _does not_ support transparency. Convert gif into sequence of PNGs and use fallback GetFlag method for videos with alpha
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    FillMode = FillMode.Fill,
                    Loop = true,
                    Size = Size,
                };
            }, true);
        }

        // most of the code below is stolen from osu.Game.Skinning.LegacySkinExtensions
        public Drawable GetFlag(Vector2 size)
        {
            var textures = GetTextures();

            switch (textures.Length)
            {
                case 0:
                    return Empty();

                case 1:
                    return new Sprite
                    {
                        Texture = textures[0],
                        Size = size,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill
                    };

                default:
                    var animation = new LegacySkinExtensions.SkinnableTextureAnimation
                    {
                        DefaultFrameLength = 1000f / 60f, // hardcoded 60fps for now, no framerate metadata with only sequence of images
                        Loop = true,
                        Size = size,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        FillMode = FillMode.Fill
                    };

                    foreach (var t in textures)
                        animation.AddFrame(t);

                    return animation;
            }
        }

        public Texture[] GetTextures()
        {
            if (textureStore == null)
                return Array.Empty<Texture>();

            // try loading images (fallback animated sequence of pngs, inspired by animated skin elements)
            var textures = getTextures().ToArray();

            if (textures.Length > 0)
                return textures;

            // if an animation was not allowed or not found, fall back to a sprite retrieval.
            var singleTexture = textureStore.Get($@"Flags/{flag}");

            return singleTexture != null
                ? new[] { singleTexture }
                : Array.Empty<Texture>();

#nullable enable
            IEnumerable<Texture> getTextures()
            {
                for (int i = 1; true; i++)
                {
                    Texture? texture;

                    if ((texture = textureStore.Get(getFrameName(i))) == null)
                        break;

                    yield return texture;
                }
            }
#nullable disable

            string getFrameName(int frameIndex)
            {
                string frameName = $@"Flags/{flag}_{frameIndex}";
                Logger.Log($@"Getting frame {frameIndex} for texture {flag}: " + frameName, LoggingTarget.Runtime, LogLevel.Debug);
                return frameName;
            }
        }
    }
}
