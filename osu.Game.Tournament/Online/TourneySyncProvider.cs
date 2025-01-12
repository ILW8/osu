// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Threading;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Online
{
    public partial class TourneySyncProvider : Component
    {
        private ScheduledDelegate? scheduled;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void lod()
        {
            ladder.AutoSyncSlug.BindValueChanged(_ => UpdateSyncTarget());
            // ladder.CurrentMatch.BindValueChanged();
        }

        private void UpdateSyncTarget()
        {
            scheduled?.Cancel();

            scheduled = Scheduler.AddDelayed(delegate
            {
                // fetch bans
            }, 1000, true);
        }
    }
}
