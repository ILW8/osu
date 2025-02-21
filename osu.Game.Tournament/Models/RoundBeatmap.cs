// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace osu.Game.Tournament.Models
{
    public partial class RoundBeatmap
    {
        public int ID;
        public string Mods = string.Empty;

        [JsonProperty("BeatmapInfo")]
        public TournamentBeatmap? Beatmap;

        [GeneratedRegex(@"(-?\w{2})(\d+)")]
        public static partial Regex PickBanModSlotRegex();
    }
}
