// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Tournament.Online
{
    public class PickBan
    {
        [JsonProperty(@"team")]
        public required string Team { get; set; }

        [JsonProperty(@"slot")]
        public required string Slot { get; set; }
    }

    [Serializable]
    public class TourneySyncState
    {
        [JsonProperty(@"match")]
        public required string MatchID { get; set; }

        [JsonProperty("bans")]
        public required List<PickBan> Bans { get; set; }

        [JsonProperty("picks")]
        public required List<PickBan> Picks { get; set; }
    }
}
