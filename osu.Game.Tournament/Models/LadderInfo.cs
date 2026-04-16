// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Game.Rulesets;

namespace osu.Game.Tournament.Models
{
    /// <summary>
    /// Holds the complete data required to operate the tournament system.
    /// </summary>
    [Serializable]
    public class LadderInfo
    {
        public Bindable<RulesetInfo?> Ruleset = new Bindable<RulesetInfo?>();

        public BindableList<TournamentMatch> Matches = new BindableList<TournamentMatch>();
        public BindableList<TournamentRound> Rounds = new BindableList<TournamentRound>();
        public BindableList<TournamentTeam> Teams = new BindableList<TournamentTeam>();

        // only used for serialisation
        public List<TournamentProgression> Progressions = new List<TournamentProgression>();

        [JsonIgnore] // updated manually in TournamentGameBase
        public Bindable<TournamentMatch?> CurrentMatch = new Bindable<TournamentMatch?>();

        public Bindable<int> ChromaKeyWidth = new BindableInt(1024)
        {
            MinValue = 640,
            MaxValue = 1366,
        };

        public Bindable<int> PlayersPerTeam = new BindableInt(4)
        {
            MinValue = 3,
            MaxValue = 4,
        };

        public Bindable<bool> AutoProgressScreens = new BindableBool(true);

        public Bindable<bool> SplitMapPoolByMods = new BindableBool(true);

        public Bindable<bool> DisplayTeamSeeds = new BindableBool();

        /// <summary>
        /// When <c>true</c>, the tournament overlay connects to a multiplayer room via SignalR
        /// for match data instead of using file-based IPC from the stable client.
        /// </summary>
        public Bindable<bool> UseMultiplayerSpectating = new BindableBool();

        /// <summary>
        /// When <c>true</c>, mutes UI sample playback (hover/click sounds) globally.
        /// Gameplay hitsounds are unaffected as they use per-skin sample stores.
        /// </summary>
        public Bindable<bool> MuteUISounds = new BindableBool(true);

        public Bindable<double> VolumeMaster = new BindableDouble(1)
        {
            MinValue = 0,
            MaxValue = 1,
        };

        public Bindable<double> VolumeMusic = new BindableDouble(1)
        {
            MinValue = 0,
            MaxValue = 1,
        };

        public Bindable<double> VolumeEffect = new BindableDouble(1)
        {
            MinValue = 0,
            MaxValue = 1,
        };
    }
}
