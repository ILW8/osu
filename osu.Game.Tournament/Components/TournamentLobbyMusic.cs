// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Tournament.IPC;

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
        /// Tracks whether the component is currently driving music playback. Used to distinguish
        /// the legitimate <c>Idle → WaitingForClients</c> transition (continue playing) from the
        /// bug case <c>Ranking → WaitingForClients</c> (host advances before the 20s ranking
        /// window elapses — music should stay stopped).
        /// </summary>
        private bool currentlyPlaying;

        /// <summary>
        /// The WorkingBeatmap we most recently assigned to <see cref="globalBeatmap"/>. Used to
        /// detect external writers (notably <see cref="MusicController"/>'s NextTrack auto-advance,
        /// which writes back through the same bindable) — see the change-watcher in
        /// <see cref="LoadComplete"/>.
        /// </summary>
        private WorkingBeatmap? lastAssignedBeatmap;

        /// <summary>
        /// Set during <see cref="Dispose"/> so the <see cref="MusicController.AllowTrackControl"/>
        /// reverter (which normally forces the value back to <c>false</c>) leaves the final
        /// teardown <c>true</c> write alone.
        /// </summary>
        private bool disposing;

        /// <summary>
        /// What the trigger model says to do for the current (connected, state, resolved) tuple.
        /// </summary>
        public enum PlaybackAction
        {
            /// <summary>Start playing if not already playing. Lobby state.</summary>
            Play,

            /// <summary>If already playing, keep playing; otherwise stay silent. Bridge state.</summary>
            ContinueOnly,

            /// <summary>Stop playing if currently playing. Gameplay / pre-lobby state.</summary>
            Stop,
        }

        /// <summary>
        /// Pure decision: what should playback do for the given inputs?
        /// </summary>
        /// <remarks>
        /// <para><see cref="TourneyState.Idle"/> is the only state that can <em>start</em> music — the
        /// post-results lobby window after <see cref="MultiplayerMatchIPCInfo.RANKING_TO_IDLE_DELAY_MS"/>
        /// has elapsed.</para>
        /// <para><see cref="TourneyState.WaitingForClients"/> is a bridge: if music was already
        /// playing (came from Idle), it should keep going while clients load the next map. If
        /// music was not playing (came from Ranking because the host advanced before the 20s
        /// reset timer fired), it should stay stopped — defensive against premature WFC entry.</para>
        /// <para>All other states (Playing, Ranking, Initialising) request stop.</para>
        /// <para><paramref name="hasActiveSpectatorPlayers"/> overrides everything to <see cref="PlaybackAction.Stop"/>:
        /// while any player is actively playing, the spectator master clock drives the (shared, cached)
        /// beatmap track directly, so lobby music must neither play nor re-arm <c>Track.Looping</c> — even
        /// if <see cref="TourneyState.Playing"/> was never observed (the server's GameplayStarted event
        /// can be missed by a late-joining spectator, leaving State stuck at WaitingForClients).</para>
        /// </remarks>
        public static PlaybackAction Decide(bool isConnected, TourneyState state, bool hasResolvedBeatmap, bool hasActiveSpectatorPlayers)
        {
            if (!isConnected || !hasResolvedBeatmap)
                return PlaybackAction.Stop;

            if (hasActiveSpectatorPlayers)
                return PlaybackAction.Stop;

            switch (state)
            {
                case TourneyState.Idle:
                    return PlaybackAction.Play;

                case TourneyState.WaitingForClients:
                    return PlaybackAction.ContinueOnly;

                default:
                    return PlaybackAction.Stop;
            }
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

            // Disable MusicController's user-control / auto-advance paths. Two random-track
            // hazards otherwise fire on us in tournament mode:
            //
            //   (a) MusicController.onTrackCompleted — subscribed via DrawableTrack.Completed
            //       inside changeTrack(). When the gameplay track ends naturally during the
            //       Ranking window (master clock keeps driving the WorkingBeatmap.Track past
            //       the last hit object), the event fires; with Looping=false (we clear it in
            //       stopPlayback so the gameplay clock doesn't loop the track) and
            //       AllowTrackControl=true, it calls NextTrack(allowProtectedTracks: true) —
            //       picking a random track from the user's library.
            //
            //   (b) MusicController.EnsurePlayingSomething — falls through to NextTrack when
            //       CurrentTrack.IsDummyDevice, which can happen during a brief window before
            //       changeTrack swaps in the real DrawableTrack.
            //
            // The tournament UI never wants either behaviour: lobby music is owned by this
            // component, never user-driven. Our own Play()/Stop() calls don't check
            // AllowTrackControl (they pass requestedByUser=false), so they keep working.
            //
            // Setting once is not enough: each PlayerArea hosts its own nested OsuScreenStack,
            // and an entering nested OsuScreen (e.g. a results screen with
            // AllowGlobalTrackControl=true) re-writes AllowTrackControl=true via
            // OsuScreen.OnEntering. We bind a reverter so any such external flip is immediately
            // undone.
            bool previousAllowTrackControl = music.AllowTrackControl.Value;
            music.AllowTrackControl.Value = false;
            Logger.Log($"[TournamentLobbyMusic] MusicController.AllowTrackControl: {previousAllowTrackControl} -> false (suppress auto-advance on track completion)");

            music.AllowTrackControl.BindValueChanged(c =>
            {
                if (disposing || !c.NewValue)
                    return;

                Logger.Log("[TournamentLobbyMusic] AllowTrackControl was flipped to True externally (likely a nested OsuScreen entering); forcing back to False");
                music.AllowTrackControl.Value = false;
            });

            // Subscribe to globalBeatmap changes from any source. If we see a change whose new
            // value isn't what we last assigned, something else (MusicController.NextTrack
            // writing back, another component, etc.) is mutating the global beatmap. That's
            // the smoking gun for "wrong song" reports — it tells us a random/different
            // WorkingBeatmap took the spot before our music.EnsurePlayingSomething fires.
            globalBeatmap.BindValueChanged(b =>
            {
                bool external = !ReferenceEquals(b.NewValue, lastAssignedBeatmap);
                Logger.Log($"[TournamentLobbyMusic] globalBeatmap changed -> '{b.NewValue?.BeatmapInfo?.Metadata.Title}' " +
                           $"(OnlineID={b.NewValue?.BeatmapInfo?.OnlineID ?? 0}, " +
                           $"external={external}, currentlyPlaying={currentlyPlaying}, state={ipc.State.Value})");
            });

            // Surface MusicController track-change events. queuedDirection (Next/Prev) reveals
            // a NextTrack/PreviousTrack call inside MusicController; None is a beatmap-bindable
            // re-routing without a directional pick.
            music.TrackChanged += onMusicControllerTrackChanged;

            ipc.Beatmap.BindValueChanged(_ => onBeatmapChanged(), true);
            ipc.State.BindValueChanged(s =>
            {
                Logger.Log($"[TournamentLobbyMusic] ipc.State changed {s.OldValue} -> {s.NewValue}");
                applyDecision($"State {s.OldValue}->{s.NewValue}");
            });
            ipc.IsConnected.BindValueChanged(c =>
            {
                Logger.Log($"[TournamentLobbyMusic] ipc.IsConnected changed -> {c.NewValue}");

                if (!c.NewValue)
                {
                    // Disconnect path: stop music, drop the resolved beatmap.
                    hasResolvedBeatmap = false;
                    wantedBeatmapId = 0;
                }

                applyDecision($"IsConnected->{c.NewValue}");
            });

            // Authoritative "gameplay is active" signal (derived from live room state, unlike the
            // event-driven TourneyState.Playing which a late-joining spectator can miss). Stops lobby
            // music — and its Track.Looping re-arm — the moment any player starts playing.
            ipc.HasActiveSpectatorPlayers.BindValueChanged(c =>
            {
                Logger.Log($"[TournamentLobbyMusic] ipc.HasActiveSpectatorPlayers changed -> {c.NewValue}");
                applyDecision($"HasActiveSpectatorPlayers->{c.NewValue}");
            });
        }

        private void onMusicControllerTrackChanged(WorkingBeatmap newWorking, TrackChangeDirection direction)
        {
            int onlineId = newWorking.BeatmapInfo?.OnlineID ?? 0;
            string title = newWorking.BeatmapInfo?.Metadata.Title ?? "(unknown)";
            Logger.Log($"[TournamentLobbyMusic] MusicController.TrackChanged direction={direction} -> '{title}' (OnlineID={onlineId}, wanted={wantedBeatmapId}, state={ipc.State.Value})");
        }

        private void onBeatmapChanged()
        {
            int newId = ipc.Beatmap.Value?.OnlineID ?? 0;

            Logger.Log($"[TournamentLobbyMusic] onBeatmapChanged: ipc.Beatmap.OnlineID={newId} " +
                       $"(was wanted={wantedBeatmapId}, resolved={hasResolvedBeatmap}) " +
                       $"state={ipc.State.Value} connected={ipc.IsConnected.Value} currentlyPlaying={currentlyPlaying}");

            if (newId == wantedBeatmapId && hasResolvedBeatmap)
                return;

            wantedBeatmapId = newId;
            hasResolvedBeatmap = false;

            tryResolveBeatmap();
            applyDecision("Beatmap changed");
        }

        private void tryResolveBeatmap()
        {
            if (wantedBeatmapId <= 0)
                return;

            var localBeatmap = beatmapManager.QueryBeatmap(b => b.OnlineID == wantedBeatmapId);

            if (localBeatmap == null)
            {
                Logger.Log($"[TournamentLobbyMusic] Beatmap {wantedBeatmapId} not yet locally available; waiting for import");
                return;
            }

            var working = beatmapManager.GetWorkingBeatmap(localBeatmap);

            // Setting the global beatmap drives MusicController's track-change pipeline.
            // Record the assignment before the bindable fires so the external-writer detector
            // in LoadComplete recognises this as our own write.
            lastAssignedBeatmap = working;
            globalBeatmap.Value = working;

            // Loop the track from its natural start. Avoid PrepareTrackForPreview here — that
            // helper is for the main-menu preview behaviour and sets RestartPoint to the
            // beatmap's preview offset, which would cause each loop iteration to jump back to
            // the preview point instead of the beginning of the audio.
            working.Track.Looping = true;
            working.Track.RestartPoint = 0;

            hasResolvedBeatmap = true;
            Logger.Log($"[TournamentLobbyMusic] Resolved beatmap {wantedBeatmapId} ('{working.BeatmapInfo?.Metadata.Title}') for lobby playback");

            applyDecision("Beatmap resolved");
        }

        private void applyDecision(string trigger)
        {
            var action = Decide(ipc.IsConnected.Value, ipc.State.Value, hasResolvedBeatmap, ipc.HasActiveSpectatorPlayers.Value);

            bool isDummy = music.CurrentTrack.IsDummyDevice;
            bool trackRunning = music.IsPlaying;

            Logger.Log($"[TournamentLobbyMusic] applyDecision({trigger}): " +
                       $"connected={ipc.IsConnected.Value} state={ipc.State.Value} resolved={hasResolvedBeatmap} " +
                       $"wantedBeatmap={wantedBeatmapId} globalBeatmap={globalBeatmap.Value?.BeatmapInfo?.OnlineID ?? 0} " +
                       $"currentlyPlaying={currentlyPlaying} music.IsPlaying={trackRunning} " +
                       $"CurrentTrack.IsDummyDevice={isDummy} AllowTrackControl={music.AllowTrackControl.Value} " +
                       $"-> {action}");

            switch (action)
            {
                case PlaybackAction.Play:
                    startPlayback(trigger);
                    break;

                case PlaybackAction.ContinueOnly:
                    if (currentlyPlaying)
                    {
                        // Re-arm Looping in case a beatmap change cleared it (changeTrack
                        // builds a fresh DrawableTrack whose underlying Track has Looping=false
                        // until PrepareTrackForPreview is re-applied).
                        if (globalBeatmap.Value?.Track != null)
                            globalBeatmap.Value.Track.Looping = true;
                        Logger.Log("[TournamentLobbyMusic] ContinueOnly: keeping music playing");
                    }
                    else
                    {
                        Logger.Log("[TournamentLobbyMusic] ContinueOnly: staying silent (not previously playing)");
                    }

                    break;

                case PlaybackAction.Stop:
                    stopPlayback(trigger);
                    break;
            }
        }

        private void startPlayback(string trigger)
        {
            if (globalBeatmap.Value?.Track is { } track)
            {
                // Reset the mod rate adjustments (e.g. DoubleTime) gameplay bound to this shared cached
                // track and hasn't unbound yet, else the lobby loop inherits the last map's rate.
                track.RemoveAllAdjustments(AdjustableProperty.Frequency);
                track.RemoveAllAdjustments(AdjustableProperty.Tempo);

                // Re-arm looping (the stop branch clears it so gameplay clocks don't loop the track).
                track.Looping = true;
            }

            int playingId = globalBeatmap.Value?.BeatmapInfo?.OnlineID ?? 0;
            string title = globalBeatmap.Value?.BeatmapInfo?.Metadata.Title ?? "(unknown)";
            Logger.Log($"[TournamentLobbyMusic] startPlayback({trigger}): EnsurePlayingSomething for beatmap {playingId} '{title}' (was playing: {currentlyPlaying})");

            music.EnsurePlayingSomething();
            currentlyPlaying = true;
        }

        private void stopPlayback(string trigger)
        {
            if (music.IsPlaying)
            {
                int playingId = globalBeatmap.Value?.BeatmapInfo?.OnlineID ?? 0;
                Logger.Log($"[TournamentLobbyMusic] stopPlayback({trigger}): Stop (was playing beatmap {playingId})");
                // Stop (not Pause) so MusicController doesn't auto-resume on screen changes
                // elsewhere — we own when music plays here.
                music.Stop();
            }
            else if (currentlyPlaying)
            {
                Logger.Log($"[TournamentLobbyMusic] stopPlayback({trigger}): clearing flag (music was not running)");
            }

            // Clear the looping flag we set in PrepareTrackForPreview, so the gameplay master
            // clock doesn't loop the same track when it takes ownership for the upcoming round.
            if (globalBeatmap.Value?.Track != null)
                globalBeatmap.Value.Track.Looping = false;

            currentlyPlaying = false;
        }

        protected override void Dispose(bool isDisposing)
        {
            disposing = true;

            base.Dispose(isDisposing);
            realmSubscription?.Dispose();

            if (music.IsNotNull())
            {
                music.TrackChanged -= onMusicControllerTrackChanged;

                if (music.IsPlaying)
                    music.Stop();

                // Restore AllowTrackControl so the regular client behaviour is intact if this
                // component is somehow recreated without a full process restart (e.g. in tests).
                // The `disposing` guard above prevents the reverter from undoing this write.
                music.AllowTrackControl.Value = true;
            }
        }
    }
}
