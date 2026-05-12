// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Tournament.Models
{
    public class RoundBeatmap
    {
        public int ID;
        public string Mods = string.Empty;
        public string SlotName = string.Empty;

        /// <summary>
        /// Per-map mod settings, keyed by mod acronym then by snake_case setting name.
        /// Value type is <c>object</c> to mirror <see cref="osu.Game.Online.API.APIMod.Settings"/>
        /// so the factory can route entries through <c>APIMod.ToMod</c> without a numeric-only
        /// path. Newtonsoft round-trips nested <c>Dictionary&lt;string, object&gt;</c> natively in
        /// <c>bracket.json</c>. Default-empty so older bracket files load unchanged.
        /// Example for a 1.5× DT map: <c>{ "DT": { "speed_change": 1.5 } }</c>.
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> ModParameters
            = new Dictionary<string, Dictionary<string, object>>();

        [JsonProperty("BeatmapInfo")]
        public TournamentBeatmap? Beatmap;
    }
}
