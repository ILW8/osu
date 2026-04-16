// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Game.Scoring;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;

namespace osu.Game.Screens.OnlinePlay.Multiplayer.Spectate
{
    /// <summary>
    /// Used to load a single <see cref="MultiSpectatorPlayer"/> in a <see cref="MultiSpectatorScreen"/>.
    /// </summary>
    public partial class MultiSpectatorPlayerLoader : SpectatorPlayerLoader
    {
        public MultiSpectatorPlayerLoader(Score score, Func<MultiSpectatorPlayer> createPlayer)
            : base(score, createPlayer)
        {
        }

        // Spectator loaders should not gate on window focus, hover state, or overlays.
        // The base PlayerLoader requires host.IsActive (window focused) which blocks
        // the tournament overlay when the operator alt-tabs.
        protected override bool ReadyForGameplay => true;

        // Skip the metadata display delay — spectators don't need to read map info.
        protected override double PlayerPushDelay => 0;
        protected override double ContentOutDelay => 250;

        [BackgroundDependencyLoader]
        private void load()
        {
            PlayerSettings.Expire();
        }

        protected override void LogoArriving(OsuLogo logo, bool resuming)
        {
        }

        protected override void LogoExiting(OsuLogo logo)
        {
        }
    }
}
