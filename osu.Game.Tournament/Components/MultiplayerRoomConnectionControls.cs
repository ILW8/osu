// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.IPC;
using osuTK;

namespace osu.Game.Tournament.Components
{
    public partial class MultiplayerRoomConnectionControls : FillFlowContainer
    {
        private readonly MultiplayerMatchIPCInfo multiplayerIpc;

        private OsuTextBox roomIdTextBox = null!;
        private OsuPasswordTextBox passwordTextBox = null!;

        public MultiplayerRoomConnectionControls(MultiplayerMatchIPCInfo multiplayerIpc)
        {
            this.multiplayerIpc = multiplayerIpc;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(0, 5f);
        }

        public override bool AcceptsFocus => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            TourneyButton connectButton;
            HoldToConfirmTourneyButton disconnectButton;
            TourneyButton reconnectButton;
            TournamentSpriteText statusText;

            Children = new Drawable[]
            {
                new TournamentSpriteText
                {
                    Text = "Multiplayer Room",
                    Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 16),
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                },
                roomIdTextBox = new OsuTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    PlaceholderText = "Room ID",
                    TabbableContentContainer = this,
                },
                passwordTextBox = new OsuPasswordTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    PlaceholderText = "Password (optional)",
                    TabbableContentContainer = this,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Children = new Drawable[]
                    {
                        connectButton = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Connect",
                            Action = performConnect,
                        },
                        disconnectButton = new HoldToConfirmTourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Disconnect",
                            Alpha = 0,
                            Enabled = { Value = false },
                            ConfirmAction = () => fireAndForget(multiplayerIpc.Disconnect()),
                        },
                    },
                },
                reconnectButton = new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Reconnect",
                    Action = () => fireAndForget(multiplayerIpc.Reconnect()),
                },
                statusText = new TournamentSpriteText
                {
                    Text = "Disconnected",
                    Font = OsuFont.GetFont(size: 12),
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Colour = OsuColour.Gray(0.6f),
                },
            };

            roomIdTextBox.OnCommit += (_, _) => performConnect();
            passwordTextBox.OnCommit += (_, _) => performConnect();

            multiplayerIpc.IsConnected.BindValueChanged(connected =>
            {
                connectButton.Enabled.Value = !connected.NewValue;
                disconnectButton.Enabled.Value = connected.NewValue;
                reconnectButton.Enabled.Value = connected.NewValue;

                connectButton.FadeTo(connected.NewValue ? 0 : 1, 200);
                disconnectButton.FadeTo(connected.NewValue ? 1 : 0, 200);

                statusText.Text = connected.NewValue
                    ? $"Connected (Room {multiplayerIpc.ConnectedRoomId.Value})"
                    : "Disconnected";
                statusText.Colour = connected.NewValue ? Colour4.LightGreen : OsuColour.Gray(0.6f);
            }, true);

            multiplayerIpc.ConnectionError.BindValueChanged(error =>
            {
                if (error.NewValue != null)
                {
                    statusText.Text = error.NewValue;
                    statusText.Colour = Colour4.OrangeRed;
                }
            });

            TextFlowContainer inviteText;
            TourneyButton acceptButton;
            TourneyButton dismissButton;

            AddRange(new Drawable[]
            {
                inviteText = new TextFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Colour = Colour4.Orange,
                    Alpha = 0,
                },
                acceptButton = new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Accept invite",
                    Alpha = 0,
                    Action = multiplayerIpc.AcceptPendingInvite,
                },
                dismissButton = new TourneyButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = "Dismiss",
                    Alpha = 0,
                    Action = multiplayerIpc.DismissPendingInvite,
                },
            });

            multiplayerIpc.PendingInvite.BindValueChanged(invite =>
            {
                if (invite.NewValue != null)
                {
                    inviteText.Clear();
                    inviteText.AddText($"Invite to room {invite.NewValue.RoomId} ({invite.NewValue.InviterName})",
                        s => s.Font = OsuFont.GetFont(size: 12));
                    inviteText.FadeIn(200);
                    acceptButton.FadeIn(200);
                    dismissButton.FadeIn(200);
                }
                else
                {
                    inviteText.FadeOut(200);
                    acceptButton.FadeOut(200);
                    dismissButton.FadeOut(200);
                }
            }, true);
        }

        protected override void OnFocus(FocusEvent e)
        {
            base.OnFocus(e);
            GetContainingFocusManager()?.ChangeFocus(roomIdTextBox);
        }

        private void performConnect()
        {
            if (multiplayerIpc.IsConnected.Value)
                return;

            if (!long.TryParse(roomIdTextBox.Text, out long roomId))
            {
                Logger.Log("[MultiplayerRoomConnectionControls] Invalid room ID", LoggingTarget.Runtime, LogLevel.Error);
                return;
            }

            string? password = string.IsNullOrEmpty(passwordTextBox.Text) ? null : passwordTextBox.Text;
            fireAndForget(multiplayerIpc.Connect(roomId, password));
        }

        private static void fireAndForget(Task task)
        {
            task.ContinueWith(t =>
            {
                Logger.Log(t.Exception?.GetBaseException().ToString() ?? "Room connection task failed.", LoggingTarget.Runtime, LogLevel.Error);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private partial class HoldToConfirmTourneyButton : TourneyButton
        {
            private const double hold_duration = 500;

            public Action? ConfirmAction { get; init; }

            private Box progressBox = null!;
            private bool confirming;

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                BackgroundColour = colours.DangerousButtonColour;

                Content.Add(progressBox = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 0,
                    Blending = BlendingParameters.Additive,
                    Depth = 0,
                });
            }

            protected override bool OnClick(ClickEvent e) => true;

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (Enabled.Value)
                    beginConfirm();

                return base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                abortConfirm();
                base.OnMouseUp(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                abortConfirm();
                base.OnHoverLost(e);
            }

            private void beginConfirm()
            {
                if (confirming)
                    return;

                confirming = true;

                progressBox.ClearTransforms();
                progressBox.ResizeWidthTo(1, hold_duration, Easing.Out).OnComplete(_ => confirm());
            }

            private void abortConfirm()
            {
                if (!confirming)
                    return;

                confirming = false;

                progressBox.ClearTransforms();
                progressBox.ResizeWidthTo(0, 200, Easing.InSine);
            }

            private void confirm()
            {
                if (!confirming)
                    return;

                confirming = false;
                progressBox.ResizeWidthTo(0);
                ConfirmAction?.Invoke();
            }
        }
    }
}
