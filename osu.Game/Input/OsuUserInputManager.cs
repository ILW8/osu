// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Diagnostics;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges.Events;
using osu.Framework.Logging;
using osuTK.Input;

namespace osu.Game.Input
{
    public partial class OsuUserInputManager : UserInputManager
    {
        private readonly Stopwatch stopWatch = new Stopwatch();
        private int moveEvents;

        internal OsuUserInputManager()
        {
            stopWatch.Start();
        }

        ~OsuUserInputManager()
        {
            stopWatch.Stop();
        }

        protected override void HandleMousePositionChange(MousePositionChangeEvent e)
        {
            moveEvents++;
            long elapsed;

            if ((elapsed = stopWatch.ElapsedMilliseconds) <= 1000)
            {
                base.HandleMousePositionChange(e);
                return;
            }

            Logger.Log($"{moveEvents} MouseMove events in {elapsed}ms: {moveEvents * 1000 / elapsed}", LoggingTarget.Information, LogLevel.Important);
            stopWatch.Restart();
            moveEvents = 0;

            base.HandleMousePositionChange(e);
        }

        protected override MouseButtonEventManager CreateButtonEventManagerFor(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right:
                    return new RightMouseManager(button);
            }

            return base.CreateButtonEventManagerFor(button);
        }

        private class RightMouseManager : MouseButtonEventManager
        {
            public RightMouseManager(MouseButton button)
                : base(button)
            {
            }

            public override bool EnableDrag => true; // allow right-mouse dragging for absolute scroll in scroll containers.
            public override bool EnableClick => false;
            public override bool ChangeFocusOnClick => false;
        }
    }
}
