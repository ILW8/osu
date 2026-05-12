// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Builds the configured <see cref="Mod"/> list rendered on a <see cref="RoundBeatmap"/>'s
    /// panel. Parses <see cref="RoundBeatmap.Mods"/> as a concatenation of 2-character acronyms
    /// (e.g. <c>"HDDT"</c> → <c>["HD", "DT"]</c>) and applies any per-acronym entries from
    /// <see cref="RoundBeatmap.ModParameters"/> by routing through <see cref="APIMod.ToMod"/> —
    /// the same path the multiplayer client uses to materialise mods from API JSON.
    /// </summary>
    public static class RoundBeatmapModFactory
    {
        public static IReadOnlyList<Mod> ConstructMods(RoundBeatmap rb, Ruleset ruleset)
        {
            var result = new List<Mod>();

            foreach (string acronym in ParseModString(rb.Mods))
            {
                Mod? mod = ruleset.CreateModFromAcronym(acronym);
                if (mod == null)
                    continue;

                if (rb.ModParameters.TryGetValue(acronym, out var settings) && settings.Count > 0)
                {
                    var api = new APIMod
                    {
                        Acronym = acronym,
                        Settings = new Dictionary<string, object>(settings),
                    };
                    mod = api.ToMod(ruleset);
                }

                result.Add(mod);
            }

            return result;
        }

        /// <summary>
        /// Split <paramref name="mods"/> into 2-character acronyms.
        /// Trailing odd characters (length not a multiple of 2) are dropped; this is the
        /// established tournament convention (all bracket-relevant osu! mods are 2 chars).
        /// </summary>
        internal static IEnumerable<string> ParseModString(string mods)
        {
            if (string.IsNullOrEmpty(mods))
                yield break;

            for (int i = 0; i + 2 <= mods.Length; i += 2)
                yield return mods.Substring(i, 2);
        }
    }
}
