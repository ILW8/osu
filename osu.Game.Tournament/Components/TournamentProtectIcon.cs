// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Corner-badge protect indicator used on <see cref="TournamentBeatmapPanel"/>.
    /// Renders as a 45°-rotated coloured wedge anchored top-right, with a shield icon
    /// inset slightly toward the centre. Tint follows the protecting team.
    /// </summary>
    public partial class TournamentProtectIcon : Container
    {
        private readonly Box backgroundWedge;
        private readonly SpriteIcon shield;

        private TeamColour? teamColour;

        /// <summary>
        /// The team protecting this beatmap. Setting to <c>null</c> hides the icon
        /// (the corner badge fades out); setting to a team colour reveals + tints.
        /// </summary>
        public TeamColour? TeamColour
        {
            get => teamColour;
            set
            {
                teamColour = value;

                if (value == null)
                {
                    Alpha = 0;
                    return;
                }

                Alpha = 1;
                backgroundWedge.Colour = TournamentGame.GetTeamColour(value.Value);
            }
        }

        public TournamentProtectIcon()
        {
            Alpha = 0;
            Masking = false;

            Children = new Drawable[]
            {
                backgroundWedge = new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Rotation = 45f,
                },
                shield = new SpriteIcon
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    RelativePositionAxes = Axes.Both,
                    Position = new Vector2(-0.14f, 0.14f),
                    Size = new Vector2(0.4f, 0.4f),
                    RelativeSizeAxes = Axes.Both,
                    Icon = FontAwesome.Solid.ShieldAlt,
                    Colour = TournamentGame.ELEMENT_BACKGROUND_COLOUR,
                },
            };
        }
    }
}
