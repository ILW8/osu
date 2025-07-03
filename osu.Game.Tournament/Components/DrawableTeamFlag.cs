// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Tournament.Models;
using osu.Game.Users.Drawables;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class UserAvatarFlagHybrid : CompositeDrawable
    {
        private readonly TournamentTeam? team;
        private readonly LadderInfo ladder;
        private Sprite flagSprite = null!;
        private DrawableAvatar? avatar;

        public UserAvatarFlagHybrid(TournamentTeam? team, LadderInfo ladder)
        {
            this.team = team;
            this.ladder = ladder;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = flagSprite = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill
            };

            if (team != null)
            {
                flagSprite.Texture = textures.Get($@"Flags/{team.FlagName}");
                team.FlagName.BindValueChanged(_ => flagSprite.Texture = textures.Get($@"Flags/{team.FlagName}"), true);
                team.Players.BindCollectionChanged((_, _) => loadAvatar(), true);
            }

            loadAvatar();
            updateVisibility();
            ladder.Use1V1Mode.BindValueChanged(_ => updateVisibility(), true);
        }

        private void updateVisibility()
        {
            if (ladder.Use1V1Mode.Value)
            {
                flagSprite.Hide();
                avatar?.Show();
            }
            else
            {
                flagSprite.Show();
                avatar?.Hide();
            }
        }

        private void loadAvatar()
        {
            var firstPlayer = team?.Players.FirstOrDefault();

            if (firstPlayer == null) return;

            avatar?.Expire();
            avatar = null;

            LoadComponentAsync(new DrawableAvatar(firstPlayer.ToAPIUser())
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill
            }, loadedAvatar =>
            {
                avatar = loadedAvatar;
                AddInternal(avatar);
                updateVisibility();
            });
        }
    }

    public partial class DrawableTeamFlag : Container
    {
        private readonly TournamentTeam? team;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [UsedImplicitly]
        private Bindable<string>? flag;

        private Sprite? overlayFlag;

        public DrawableTeamFlag(TournamentTeam? team)
        {
            this.team = team;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            if (team == null) return;

            Size = ladder.Use1V1Mode.Value ? new Vector2(75) : new Vector2(75, 54);

            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 5,
                    Child = new UserAvatarFlagHybrid(team, ladder)
                    {
                        RelativeSizeAxes = Axes.Both
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
                overlayFlag.Texture = textures.Get($@"Flags/{team.Players.FirstOrDefault()?.CountryCode}");
            }, true);

            team?.Players.BindCollectionChanged((_, _) =>
            {
                if (ladder.Use1V1Mode.Value)
                    overlayFlag.Show();
                else
                    overlayFlag.Hide();
            }, true);
        }
    }
}
