// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Tournament.Screens.Setup
{
    internal partial class SyncSlugSelector : ActionableInfo, IHasCurrentValue<string?>
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

            Action = () =>
            {
                if (string.IsNullOrEmpty(slugInputBox.Text))
                {
                    Current.Value = null;
                    return;
                }

                Current.Value = slugInputBox.Text;
            };

            Current.BindValueChanged(vce =>
            {
                if (string.IsNullOrEmpty(vce.NewValue))
                {
                    Value = "[disabled]";
                    return;
                }

                Value = vce.NewValue;
                slugInputBox.Text = vce.NewValue;
            }, true);

            return drawable;
        }

        private readonly BindableWithCurrent<string?> current = new BindableWithCurrent<string?>();

        public Bindable<string?> Current
        {
            get => current.Current;
            set => current.Current = value;
        }
    }
}

