// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Logging;
using osu.Framework.Timing;
using osu.Game.Screens.Play;

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
        private const double catchup_rate = 2;

        private readonly GameplayClockContainer masterClock;

        /// <summary>The user this clock is spectating (logging only).</summary>
        public readonly int UserId;

        public double CurrentTime { get; private set; }

        /// <summary>
        /// Whether this clock is waiting on frames to continue playback.
        /// </summary>
        public bool WaitingOnFrames { get; set; } = true;

        /// <summary>
        /// Whether this clock is behind the master clock and running at a higher rate to catch up to it.
        /// </summary>
        /// <remarks>
        /// Of note, this will be false if this clock is *ahead* of the master clock.
        /// </remarks>
        public bool IsCatchingUp { get; set; }

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

        public SpectatorPlayerClock(GameplayClockContainer masterClock, int userId = 0)
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

        public double Rate
        {
            get => IsCatchingUp ? catchup_rate : 1;
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

                double elapsed = elapsedSource * Rate;

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
