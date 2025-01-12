// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Tournament.Screens.Setup
{
    internal partial class SyncSlugSelector : ActionableInfo
    {
        // public new Action<int>? Action;

        private OsuTextBox? slugInputBox;

        protected override Drawable CreateComponent()
        {
            var drawable = base.CreateComponent();
            FlowContainer.Insert(-1, slugInputBox = new OsuTextBox
            {
                Text = "",
                Width = 100
            });

            Value = "[disabled]";

            base.Action = () =>
            {
                if (string.IsNullOrEmpty(slugInputBox.Text))
                {
                    Value = "[disabled]";
                    return;
                }

                Value = slugInputBox.Text;
                //
                // // box contains text
                // if (int.TryParse(numberBox.Text, out int number))
                // {
                //     number = Math.Clamp(number, minimum_window_height, maximum_window_height);
                // }
                //
                // // in case number got clamped, reset number in numberBox
                // numberBox.Text = number.ToString();
                //
                // Action?.Invoke(number);
            };
            return drawable;
        }
    }
}

