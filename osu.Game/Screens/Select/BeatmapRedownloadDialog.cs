// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Screens.Select
{
    public partial class BeatmapRedownloadDialog : DangerousActionDialog
    {
        private readonly BeatmapSetInfo beatmapSet;

        [Resolved]
        private BeatmapSetOverlay beatmapOverlay { get; set; } = null!;

        public BeatmapRedownloadDialog(BeatmapSetInfo beatmapSet)
        {
            this.beatmapSet = beatmapSet;
            BodyText = $"{beatmapSet.Metadata.Artist} - {beatmapSet.Metadata.Title}\nThis beatmapset will be redownloaded";
        }

        private void downloadFailed(ArchiveDownloadRequest<IBeatmapSetInfo> request) => Schedule(() => beatmapOverlay.FetchAndShowBeatmapSet(beatmapSet.OnlineID));

        [BackgroundDependencyLoader]
        private void load(BeatmapManager beatmapManager, OsuConfigManager config, BeatmapModelDownloader beatmaps, BeatmapSetOverlay beatmapOverlay)
        {
            DangerousAction = () =>
            {
                if (!beatmapManager.Delete(beatmapSet)) return;

                beatmaps.DownloadFailed += downloadFailed;
                beatmaps.Download(beatmapSet, config.GetBindable<bool>(OsuSetting.PreferNoVideo).Value);
            };
        }
    }
}
