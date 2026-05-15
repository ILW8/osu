// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Tournament.IPC;
using Realms;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Plays the current multiplayer-room beatmap on loop while the tournament
    /// overlay is connected to a room and gameplay isn't actively rendered.
    /// </summary>
    public partial class TournamentLobbyMusic : Component
    {
        [Resolved]
        private MultiplayerMatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private Bindable<WorkingBeatmap> globalBeatmap { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        private IDisposable? realmSubscription;

        /// <summary>
        /// The online ID of the beatmap we last attempted to resolve. Read on the update thread.
        /// </summary>
        private int wantedBeatmapId;

        /// <summary>
        /// True iff the wanted beatmap has been successfully turned into a global <see cref="WorkingBeatmap"/>.
        /// </summary>
        private bool hasResolvedBeatmap;

        /// <summary>
        /// Pure decision: should we be playing lobby music right now?
        /// Music plays only when connected to a room with a locally-resolved beatmap
        /// and the IPC state is Idle or WaitingForClients. Other states either have
        /// the gameplay master clock driving audio (Playing) or are transitional
        /// non-lobby states (Ranking, Initialising) where music shouldn't kick in.
        /// </summary>
        public static bool ShouldPlay(bool isConnected, TourneyState state, bool hasResolvedBeatmap)
        {
            if (!isConnected || !hasResolvedBeatmap)
                return false;

            return state == TourneyState.Idle || state == TourneyState.WaitingForClients;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Retry beatmap resolution when local imports land — handles the case where
            // ipc.Beatmap is set before MultiplayerMatchIPCInfo.ensureBeatmapDownloaded
            // finishes pulling the set.
            realmSubscription = realm.RegisterForNotifications(
                r => r.All<BeatmapSetInfo>().Where(s => !s.DeletePending),
                (_, _) =>
                {
                    if (!hasResolvedBeatmap && wantedBeatmapId > 0)
                        tryResolveBeatmap();
                });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ipc.Beatmap.BindValueChanged(_ => onBeatmapChanged(), true);
            ipc.State.BindValueChanged(_ => applyDecision());
            ipc.IsConnected.BindValueChanged(c =>
            {
                if (!c.NewValue)
                {
                    // Disconnect path: stop music, drop the resolved beatmap.
                    hasResolvedBeatmap = false;
                    wantedBeatmapId = 0;
                }

                applyDecision();
            });
        }

        private void onBeatmapChanged()
        {
            int newId = ipc.Beatmap.Value?.OnlineID ?? 0;

            if (newId == wantedBeatmapId && hasResolvedBeatmap)
                return;

            wantedBeatmapId = newId;
            hasResolvedBeatmap = false;

            tryResolveBeatmap();
            applyDecision();
        }

        private void tryResolveBeatmap()
        {
            if (wantedBeatmapId <= 0)
                return;

            var localBeatmap = beatmapManager.QueryBeatmap(b => b.OnlineID == wantedBeatmapId);

            if (localBeatmap == null)
                return;

            var working = beatmapManager.GetWorkingBeatmap(localBeatmap);

            // Setting the global beatmap drives MusicController's track-change pipeline.
            globalBeatmap.Value = working;
            working.PrepareTrackForPreview(looping: true);

            hasResolvedBeatmap = true;
            Logger.Log($"[TournamentLobbyMusic] Resolved beatmap {wantedBeatmapId} for lobby playback");

            applyDecision();
        }

        private void applyDecision()
        {
            if (ShouldPlay(ipc.IsConnected.Value, ipc.State.Value, hasResolvedBeatmap))
            {
                music.EnsurePlayingSomething();
            }
            else
            {
                // Stop (not Pause) so MusicController doesn't auto-resume on screen
                // changes elsewhere — we own when music plays here.
                if (music.IsPlaying)
                    music.Stop();

                // Clear the looping flag we set in PrepareTrackForPreview, so the
                // gameplay master clock doesn't loop the same track when it takes
                // ownership for the upcoming round.
                if (globalBeatmap.Value?.Track != null)
                    globalBeatmap.Value.Track.Looping = false;
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            realmSubscription?.Dispose();

            if (music.IsNotNull() && music.IsPlaying)
                music.Stop();
        }
    }
}
