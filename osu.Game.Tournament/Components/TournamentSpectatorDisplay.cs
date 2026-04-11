// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.TeamVersus;
using osu.Game.Online.Spectator;
using osu.Game.Tournament.IPC;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Displays live match information from a multiplayer room, replacing the chroma key area
    /// when spectating via <see cref="MultiplayerMatchIPCInfo"/>.
    /// Shows player names and individual scores grouped by team.
    /// </summary>
    public partial class TournamentSpectatorDisplay : CompositeDrawable
    {
        [Resolved]
        private MultiplayerClient multiplayerClient { get; set; } = null!;

        [Resolved]
        private SpectatorClient spectatorClient { get; set; } = null!;

        private readonly MultiplayerMatchIPCInfo multiplayerIpc;
        private FillFlowContainer team1Flow = null!;
        private FillFlowContainer team2Flow = null!;

        private readonly Dictionary<int, TournamentSpriteText> playerScoreTexts = new Dictionary<int, TournamentSpriteText>();

        public TournamentSpectatorDisplay(MultiplayerMatchIPCInfo multiplayerIpc)
        {
            this.multiplayerIpc = multiplayerIpc;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                // Left team panel
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 20, 200),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(10),
                            Children = new Drawable[]
                            {
                                new TournamentSpriteText
                                {
                                    Text = "Team Red",
                                    Font = OsuFont.Torus.With(size: 20, weight: FontWeight.Bold),
                                    Colour = TournamentGame.COLOUR_RED,
                                },
                                team1Flow = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new osuTK.Vector2(0, 4),
                                },
                            }
                        },
                    }
                },
                // Right team panel
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.5f,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 20, 200),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding(10),
                            Children = new Drawable[]
                            {
                                new TournamentSpriteText
                                {
                                    Text = "Team Blue",
                                    Font = OsuFont.Torus.With(size: 20, weight: FontWeight.Bold),
                                    Colour = TournamentGame.COLOUR_BLUE,
                                },
                                team2Flow = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new osuTK.Vector2(0, 4),
                                },
                            }
                        },
                    }
                },
            };

            multiplayerIpc.IsConnected.BindValueChanged(connected =>
            {
                if (connected.NewValue)
                    refreshPlayerList();
                else
                    clearPlayerList();
            }, true);

            multiplayerClient.UserJoined += onUserChanged;
            multiplayerClient.UserLeft += onUserChanged;
            multiplayerClient.UserKicked += onUserChanged;
            spectatorClient.OnNewFrames += onNewFrames;
        }

        private void onUserChanged(MultiplayerRoomUser _)
        {
            Schedule(refreshPlayerList);
        }

        private void onNewFrames(int userId, FrameDataBundle bundle)
        {
            Schedule(() =>
            {
                if (playerScoreTexts.TryGetValue(userId, out var text))
                    text.Text = $"{bundle.Header.TotalScore:N0}";
            });
        }

        private void refreshPlayerList()
        {
            if (multiplayerClient.Room == null)
                return;

            playerScoreTexts.Clear();

            var team1Users = new List<MultiplayerRoomUser>();
            var team2Users = new List<MultiplayerRoomUser>();

            foreach (var user in multiplayerClient.Room.Users)
            {
                if (user.State == MultiplayerUserState.Spectating)
                    continue;

                if (user.MatchState is TeamVersusUserState teamState)
                {
                    if (teamState.TeamID == 0)
                        team1Users.Add(user);
                    else
                        team2Users.Add(user);
                }
            }

            populateTeamFlow(team1Flow, team1Users);
            populateTeamFlow(team2Flow, team2Users);
        }

        private void populateTeamFlow(FillFlowContainer flow, List<MultiplayerRoomUser> users)
        {
            flow.Clear();

            foreach (var user in users)
            {
                var scoreText = new TournamentSpriteText
                {
                    Text = "0",
                    Font = OsuFont.Torus.With(size: 16),
                    Colour = Colour4.White,
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                };

                playerScoreTexts[user.UserID] = scoreText;

                flow.Add(new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new TournamentSpriteText
                        {
                            Text = user.User?.Username ?? $"User {user.UserID}",
                            Font = OsuFont.Torus.With(size: 16),
                            Colour = Colour4.White,
                        },
                        scoreText,
                    }
                });
            }
        }

        private void clearPlayerList()
        {
            team1Flow.Clear();
            team2Flow.Clear();
            playerScoreTexts.Clear();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (multiplayerClient.IsNotNull())
            {
                multiplayerClient.UserJoined -= onUserChanged;
                multiplayerClient.UserLeft -= onUserChanged;
                multiplayerClient.UserKicked -= onUserChanged;
            }

            if (spectatorClient.IsNotNull())
                spectatorClient.OnNewFrames -= onNewFrames;
        }
    }
}
