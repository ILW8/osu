// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Tournament.Screens.Setup
{
    internal partial class RankingChatDelaySelector : ActionableInfo
    {
        private const int minimum_chat_delay_ms = 1000;
        private const int maximum_chat_delay_ms = 12000;

        private OsuNumberBox? numberBox;

        public new Action<int>? Action;

        protected override Drawable CreateComponent()
        {
            var drawable = base.CreateComponent();
            FlowContainer.Insert(-1, numberBox = new OsuNumberBox
            {
                Text = "10000",
                Width = 120
            });

            base.Action = () =>
            {
                if (string.IsNullOrEmpty(numberBox.Text))
                    return;

                // box contains text
                if (!int.TryParse(numberBox.Text, out int number))
                {
                    // at this point, the only reason we can arrive here is if the input number was too big to parse into an int
                    // so clamp to max allowed value
                    number = maximum_chat_delay_ms;
                }
                else
                {
                    number = Math.Clamp(number, minimum_chat_delay_ms, maximum_chat_delay_ms);
                }

                // in case number got clamped, reset number in numberBox
                numberBox.Text = number.ToString();

                Action?.Invoke(number);
            };
            return drawable;
        }
    }
}
