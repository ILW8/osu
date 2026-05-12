// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Components
{
    /// <summary>
    /// Mod icon displayed in tournament usages, allowing user overridden graphics.
    /// Two construction paths:
    /// <list type="bullet">
    /// <item><c>(string acronym)</c> — legacy path. Uses default-settings Mod, custom texture honoured.</item>
    /// <item><c>(Mod configuredMod)</c> — new in Phase 2. If <c>HasNonDefaultSettings</c> is true,
    /// the custom-texture lookup is skipped so the embedded <see cref="ModIcon"/>'s extender
    /// (DT rate inline as <c>1.50x</c>) and cog corner badge are surfaced.</item>
    /// </list>
    /// </summary>
    public partial class TournamentModIcon : CompositeDrawable
    {
        private readonly string modAcronym;
        private readonly Mod? configuredMod;

        [Resolved]
        private IRulesetStore rulesets { get; set; } = null!;

        public TournamentModIcon(string modAcronym)
        {
            this.modAcronym = modAcronym;
        }

        public TournamentModIcon(Mod configuredMod)
        {
            this.configuredMod = configuredMod;
            modAcronym = configuredMod.Acronym;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures, LadderInfo ladderInfo)
        {
            // Custom branding only applies when the mod is at default settings.
            // A static branded sprite cannot surface a non-default speed change / setting,
            // so non-default mods fall through to the embedded ModIcon (which paints the
            // extender + cog).
            bool allowCustomTexture = configuredMod == null || !((IMod)configuredMod).HasNonDefaultSettings;

            if (allowCustomTexture)
            {
                var customTexture = textures.Get($"Mods/{modAcronym}");

                if (customTexture != null)
                {
                    AddInternal(new Sprite
                    {
                        FillMode = FillMode.Fit,
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Texture = customTexture,
                    });

                    return;
                }
            }

            var mod = configuredMod
                      ?? rulesets.GetRuleset(ladderInfo.Ruleset.Value?.OnlineID ?? 0)
                                ?.CreateInstance().CreateModFromAcronym(modAcronym);

            if (mod == null)
                return;

            AddInternal(new ModIcon(mod, false)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(0.5f),
            });
        }
    }
}
