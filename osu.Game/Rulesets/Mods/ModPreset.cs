// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Database;
using osu.Game.Online.API;
using Realms;

namespace osu.Game.Rulesets.Mods
{
    public interface IModPreset : IHasGuidPrimaryKey, ISoftDelete
    {
        /// <summary>
        /// The ruleset that the preset is valid for.
        /// </summary>
        public RulesetInfo Ruleset { get; set; }

        /// <summary>
        /// The name of the mod preset.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The description of the mod preset.
        /// </summary>
        public string Description { get; set; }

        public ICollection<Mod> Mods { get; set; }
    }

    public class FreeModModPreset : RealmObject, IModPreset
    {
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        public RulesetInfo Ruleset { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Ignored]
        public ICollection<Mod> Mods
        {
            get
            {
                if (string.IsNullOrEmpty(ModsJson))
                    return Array.Empty<Mod>();

                var apiMods = JsonConvert.DeserializeObject<IEnumerable<APIMod>>(ModsJson);
                var ruleset = Ruleset.CreateInstance();
                return apiMods.AsNonNull().Select(mod => mod.ToMod(ruleset)).ToArray();
            }
            set
            {
                var apiMods = value.Select(mod => new APIMod(mod)).ToArray();
                ModsJson = JsonConvert.SerializeObject(apiMods);
            }
        }

        /// <summary>
        /// The set of configured mods that are part of the preset, serialised as a JSON blob.
        /// </summary>
        [MapTo("Mods")]
        public string ModsJson { get; set; } = string.Empty;

        /// <summary>
        /// Whether the preset has been soft-deleted by the user.
        /// </summary>
        public bool DeletePending { get; set; }
    }

    /// <summary>
    /// A mod preset is a named collection of configured mods.
    /// Presets are presented to the user in the mod select overlay for convenience.
    /// </summary>
    public class ModPreset : RealmObject, IModPreset
    {
        /// <summary>
        /// The internal database ID of the preset.
        /// </summary>
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        public RulesetInfo Ruleset { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Ignored]
        public ICollection<Mod> Mods
        {
            get
            {
                if (string.IsNullOrEmpty(ModsJson))
                    return Array.Empty<Mod>();

                var apiMods = JsonConvert.DeserializeObject<IEnumerable<APIMod>>(ModsJson);
                var ruleset = Ruleset.CreateInstance();
                return apiMods.AsNonNull().Select(mod => mod.ToMod(ruleset)).ToArray();
            }
            set
            {
                var apiMods = value.Select(mod => new APIMod(mod)).ToArray();
                ModsJson = JsonConvert.SerializeObject(apiMods);
            }
        }

        /// <summary>
        /// The set of configured mods that are part of the preset, serialised as a JSON blob.
        /// </summary>
        [MapTo("Mods")]
        public string ModsJson { get; set; } = string.Empty;

        /// <summary>
        /// Whether the preset has been soft-deleted by the user.
        /// </summary>
        public bool DeletePending { get; set; }
    }
}
