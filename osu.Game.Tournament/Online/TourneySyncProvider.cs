// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Online.API;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Online
{
    public partial class TourneySyncProvider : Component
    {
        private ScheduledDelegate? scheduled;

        // TODO: use BeatmapChoice instead of PickBan
        public event Action<List<PickBan>, List<PickBan>>? OnMappoolStateUpdate;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            ladder.AutoSyncSlug.BindValueChanged(_ => updateSyncTarget());
            ladder.CurrentMatch.BindValueChanged(_ => updateSyncTarget());
            ladder.AutoSyncEnabled.BindValueChanged(_ => updateSyncTarget(), true);
        }

        private void updateSyncTarget()
        {
            scheduled?.Cancel();

            string? slug = ladder.AutoSyncSlug.Value;
            string? matchID = ladder.CurrentMatch.Value?.SyncId.Value;

            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(matchID))
            {
                return;
            }

            if (ladder.AutoSyncEnabled.Value)
                scheduleFetchTask(slug, matchID);
        }

        private void scheduleFetchTask(string slug, string matchID)
        {
            scheduled?.Cancel();

            scheduled = Scheduler.AddDelayed(() => Task.Run(async () =>
            {
                await fetchLatestState(slug, matchID).ConfigureAwait(false);
                scheduleFetchTask(slug, matchID);
            }), 1000);
        }

        private async Task fetchLatestState(string slug, string matchID)
        {
            Logger.Log($"Pretend I'm fetching from https://example.com/{slug}/{matchID}", LoggingTarget.Network);
            var syncStateRequest = new OsuJsonWebRequest<TourneySyncState>($"https://lazer-state-sync.notactuallyajame.workers.dev/{slug}/{matchID}");

            try
            {
                await syncStateRequest.PerformAsync().ConfigureAwait(false);

                var latestSyncState = syncStateRequest.ResponseObject;
                if (latestSyncState == null)
                    return;

                string serializedResponse = JsonConvert.SerializeObject(latestSyncState,
                    new JsonSerializerSettings
                    {
                        Formatting = Formatting.None,
                        NullValueHandling = NullValueHandling.Include,
                        DefaultValueHandling = DefaultValueHandling.Include,
                        Converters = new JsonConverter[] { new JsonPointConverter() }
                    });

                Logger.Log($"Got latest sync state: {serializedResponse}");

                OnMappoolStateUpdate?.Invoke(latestSyncState.Picks, latestSyncState.Bans);
            }
            catch
            {
            }
        }
    }
}
