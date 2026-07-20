// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Logging;
using osu.Framework.Timing;

namespace osu.Game.Screens.OnlinePlay.Multiplayer.Spectate
{
    /// <summary>
    /// A clock which catches up using rate adjustment.
    /// </summary>
    public class SpectatorPlayerClock : IFrameBasedClock, IAdjustableClock
    {
        /// <summary>
        /// The catch up rate.
        /// </summary>
        public const double CATCHUP_RATE = 2;

        /// <summary>
        /// Essentially the opposite of <see cref="CATCHUP_RATE"/>
        /// </summary>
        private const double slow_rate = 0.5;

        private readonly IFrameBasedClock masterClock;

        /// <summary>The user this clock is spectating (logging only).</summary>
        public readonly int UserId;

        public double CurrentTime { get; private set; }

        /// <summary>
        /// Whether this clock is waiting on frames to continue playback.
        /// </summary>
        public bool WaitingOnFrames { get; set; } = true;

        /// <summary>
        /// The time of this player's most recently received replay frame (its live edge). A player with no frames
        /// reports <see cref="double.NegativeInfinity"/> so it reads as maximally behind and is treated as
        /// starved/abandoned rather than as the live edge.
        /// </summary>
        public double LatestFrameTime { get; set; } = double.NegativeInfinity;

        /// <summary>
        /// Whether this clock has been abandoned by the sync manager because its frame delivery fell too far behind
        /// the live edge. An abandoned player is excluded from master pacing so it no longer drags the cast.
        /// </summary>
        public bool Abandoned { get; set; }

        /// <summary>
        /// Whether this clock is behind the master clock and running at a higher rate to catch up to it.
        /// </summary>
        /// <remarks>
        /// Of note, this will be false if this clock is *ahead* of the master clock.
        /// </remarks>
        public bool IsCatchingUp { get; set; }

        /// <summary>
        /// Whether this clock is ahead of the master clock and running at a lower rate to let the master catch-up to it.
        /// </summary>
        /// <remarks>
        /// Mutually exclusive with <see cref="IsCatchingUp"/> and <see cref="IsHalted"/>.
        /// </remarks>
        public bool IsSlowingDown { get; set; }

        /// <summary>
        /// Whether this clock is frozen because it's too far ahead of the master to ease back smoothly, holding until
        /// the master catches up to within the sync target. While halted the clock does not advance (see
        /// <see cref="IsRunning"/>).
        /// </summary>
        /// <remarks>
        /// Mutually exclusive with <see cref="IsCatchingUp"/> and <see cref="IsSlowingDown"/>.
        /// </remarks>
        public bool IsHalted { get; set; }

        /// <summary>
        /// Whether this spectator clock should be running.
        /// Use instead of <see cref="Start"/> / <see cref="Stop"/> to control time.
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// The master clock position last consumed by <see cref="ProcessFrame"/>. Used to detect whether the master
        /// produced a new frame since we last advanced, so that being processed multiple times per host frame does
        /// not advance us more than once.
        /// </summary>
        private double lastConsumedMasterTime;

        public SpectatorPlayerClock(IFrameBasedClock masterClock, int userId = 0)
        {
            this.masterClock = masterClock;
            UserId = userId;
            lastConsumedMasterTime = masterClock.CurrentTime;
        }

        public void Reset() => CurrentTime = 0;

        public void Start()
        {
            // Our running state should only be managed by SpectatorSyncManager via IsRunning.
        }

        public void Stop()
        {
            // Our running state should only be managed by an SpectatorSyncManager via IsRunning.
        }

        public bool Seek(double position)
        {
            Logger.Log($"{nameof(SpectatorPlayerClock)} seeked to {position}");
            CurrentTime = position;
            return true;
        }

        public void ResetSpeedAdjustments()
        {
        }

        private double catchUpMultiplier => IsCatchingUp ? CATCHUP_RATE : IsSlowingDown ? slow_rate : 1;

        public double Rate
        {
            get => masterClock.Rate * catchUpMultiplier;
            set => throw new NotImplementedException();
        }

        public void ProcessFrame()
        {
            // false on a repeat ProcessFrame within the same frame
            bool masterAdvanced = masterClock.CurrentTime > lastConsumedMasterTime;
            lastConsumedMasterTime = masterClock.CurrentTime;

            if (IsRunning)
            {
                double elapsedSource;

                if (masterClock.ElapsedFrameTime != 0)
                {
                    elapsedSource = masterAdvanced ? masterClock.ElapsedFrameTime : 0;
                }
                else
                {
                    elapsedSource = Math.Clamp(masterClock.CurrentTime - CurrentTime, 0, 16);
                }

                double elapsed = elapsedSource * catchUpMultiplier;

                CurrentTime += elapsed;
                ElapsedFrameTime = elapsed;
                FramesPerSecond = masterClock.FramesPerSecond;
            }
            else
            {
                ElapsedFrameTime = 0;
                FramesPerSecond = 0;
            }
        }

        public double ElapsedFrameTime { get; private set; }

        public double FramesPerSecond { get; private set; }

        public FrameTimeInfo TimeInfo => new FrameTimeInfo { Elapsed = ElapsedFrameTime, Current = CurrentTime };
    }
}
