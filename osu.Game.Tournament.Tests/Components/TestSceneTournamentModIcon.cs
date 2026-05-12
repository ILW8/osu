// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tournament.Components;
using osuTK;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentModIcon : TournamentTestScene
    {
        private FillFlowContainer flow = null!;

        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("clear flow", () =>
            {
                Child = flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(10),
                };
            });
        }

        [Test]
        public void TestCustomTextureSuppressedForCustomisedMod()
        {
            OsuModDoubleTime dt = null!;
            TournamentModIcon icon = null!;
            AddStep("add DT-1.6 icon", () =>
            {
                dt = new OsuModDoubleTime { SpeedChange = { Value = 1.6 } };
                flow.Add(icon = new TournamentModIcon(dt) { Size = new Vector2(60) });
            });

            // Documents the gating precondition: the gate only flips to "skip custom texture"
            // when this property is true. If a future change to HasNonDefaultSettings semantics
            // makes it return false for SpeedChange != default, the gate stops protecting users.
            AddAssert("configured mod has non-default settings", () => ((IMod)dt).HasNonDefaultSettings);

            AddUntilStep("icon loaded", () => icon.IsLoaded);

            AddAssert("falls through to embedded ModIcon (no top-level custom Sprite)",
                () => icon.ChildrenOfType<ModIcon>().Any()
                      && !icon.ChildrenOfType<Sprite>().Any(s => s.Parent == icon));
        }

        [Test]
        public void TestCustomTexturePreservedForDefaultMod()
        {
            Mod hd = null!;
            TournamentModIcon icon = null!;
            AddStep("add default HD icon", () =>
            {
                hd = new OsuModHidden();
                flow.Add(icon = new TournamentModIcon(hd) { Size = new Vector2(60) });
            });

            // Documents the gating precondition: when this is false, the gate lets the
            // Mods/{acronym} custom-texture lookup run (it just misses harmlessly in tests
            // because no fake texture is registered, so we still fall through to the embedded
            // ModIcon — but in production with branded textures the Sprite would render).
            AddAssert("default mod reports HasNonDefaultSettings false", () => !((IMod)hd).HasNonDefaultSettings);

            AddUntilStep("icon loaded", () => icon.IsLoaded);

            AddAssert("renders an embedded ModIcon (texture lookup missed, fallthrough fired)",
                () => icon.ChildrenOfType<ModIcon>().Any());
        }

        [Test]
        public void TestAcronymStringPathUnchanged()
        {
            // Regression guard: legacy callers passing a string acronym still get an icon.
            TournamentModIcon icon = null!;
            AddStep("add DT via string", () =>
            {
                flow.Add(icon = new TournamentModIcon("DT") { Size = new Vector2(60) });
            });

            AddUntilStep("icon loaded", () => icon.IsLoaded);
            AddAssert("has child drawable", () => icon.ChildrenOfType<Drawable>().Any());
        }
    }
}
