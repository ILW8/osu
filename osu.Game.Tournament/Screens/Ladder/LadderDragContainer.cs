// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osuTK;

namespace osu.Game.Tournament.Screens.Ladder
{
    public partial class LadderDragContainer : Container
    {
        protected override bool OnDragStart(DragStartEvent e) => true;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        private Vector2 target;

        private float scale = 1;

        protected override bool ComputeIsMaskedAway(RectangleF maskingBounds) => false;

        public override bool UpdateSubTreeMasking() => false;

        protected override void OnDrag(DragEvent e)
        {
            this.MoveTo(target += e.Delta, 1000, Easing.OutQuint);
        }

        public void SetPosition(Vector2 position, float duration = 1000, Easing easing = Easing.OutQuint)
        {
            this.MoveTo(target = position, duration, easing);
        }

        public void AdjustPosition(Vector2 delta, float duration = 1000, Easing easing = Easing.OutQuint)
        {
            this.MoveTo(target += delta, duration, easing);
        }

        public void SetScale(float newScale, float duration = 1000, Easing easing = Easing.OutQuint)
        {
            newScale = Math.Clamp(newScale, min_scale, max_scale);

            SetPosition(target - Parent!.DrawSize / 2f * (newScale - scale));
            this.ScaleTo(scale = newScale, duration, easing);
        }

        public void AdjustScale(float scaleDelta, float duration = 1000, Easing easing = Easing.OutQuint)
        {
            SetScale(scale + scaleDelta, duration, easing);
        }

        private const float min_scale = 0.3f;
        private const float max_scale = 1.4f;

        protected override bool OnScroll(ScrollEvent e)
        {
            float newScale = Math.Clamp(scale + e.ScrollDelta.Y / 15 * scale, min_scale, max_scale);

            Logger.Log($"mousePos: {e.MousePosition}, target: {target}, scale: {scale}, newScale: {newScale}");

            this.MoveTo(target -= e.MousePosition * (newScale - scale), 1000, Easing.OutQuint);
            this.ScaleTo(scale = newScale, 1000, Easing.OutQuint);

            return true;
        }
    }
}
