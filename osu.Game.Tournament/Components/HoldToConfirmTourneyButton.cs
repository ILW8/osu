// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// A <see cref="TourneyButton"/> that requires a hold (~500ms) before its Action fires.
    /// Mirrors the composition used by <see cref="osu.Game.Overlays.Dialog.PopupDialogDangerousButton"/>: an inner
    /// <see cref="HoldToConfirmContainer"/> overlay captures mouse input and forwards to the button's normal Action only after the hold completes.
    /// </summary>
    public partial class HoldToConfirmTourneyButton : TourneyButton
    {
        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            BackgroundColour = colours.DangerousButtonColour;

            Box progressBox;

            Content.Add(progressBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0,
                Blending = BlendingParameters.Additive,
                Depth = 0,
            });

            DangerousConfirmContainer confirmContainer;

            AddInternal(confirmContainer = new DangerousConfirmContainer(() => Enabled.Value)
            {
                Action = () => Action?.Invoke(),
                RelativeSizeAxes = Axes.Both,
            });

            confirmContainer.Progress.BindValueChanged(p => progressBox.Width = (float)p.NewValue, true);
        }

        private partial class DangerousConfirmContainer : HoldToConfirmContainer
        {
            private readonly Func<bool> isEnabled;
            private bool mouseDown;

            // Without this, AbortConfirm short-circuits on Fired and leaves state stuck at
            // (confirming=true, Fired=true, progress=1), so the button cannot be re-triggered.
            // Same fix HoldToExitGameOverlay and HoldForMenuButton apply.
            protected override bool AllowMultipleFires => true;

            public DangerousConfirmContainer(Func<bool> isEnabled)
                : base(isDangerousAction: true)
            {
                this.isEnabled = isEnabled;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                if (!isEnabled())
                    return false;

                BeginConfirm();
                mouseDown = true;
                return true;
            }

            protected override void OnMouseUp(MouseUpEvent e)
            {
                if (!e.HasAnyButtonPressed)
                {
                    AbortConfirm();
                    mouseDown = false;
                }
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (mouseDown)
                    BeginConfirm();

                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);

                if (mouseDown)
                    AbortConfirm();
            }
        }
    }
}
