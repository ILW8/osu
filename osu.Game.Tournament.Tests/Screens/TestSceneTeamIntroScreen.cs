// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.TeamIntro;

namespace osu.Game.Tournament.Tests.Screens
{
    public partial class TestSceneTeamIntroScreen : TournamentScreenTestScene
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Ladder.CurrentMatch.Value = new TournamentMatch
            {
                Team1 = { Value = Ladder.Teams.FirstOrDefault(t => t.Acronym.Value == "USA") },
                Team2 = { Value = Ladder.Teams.FirstOrDefault(t => t.Acronym.Value == "JPN") },
                Round = { Value = Ladder.Rounds.FirstOrDefault(g => g.Name.Value == "Finals") }
            };

            Add(new TeamIntroScreen
            {
                FillMode = FillMode.Fit,
                FillAspectRatio = 16 / 9f
            });
        }

        [Test]
        public void TestUse1V1Display()
        {
            AddStep("disable 1v1", () => Ladder.Use1V1Mode.Value = false);
            AddAssert("renders DrawableTeamWithPlayers", () =>
                this.ChildrenOfType<DrawableTeamWithPlayers>().Count(), () => Is.EqualTo(2));

            AddStep("enable 1v1", () => Ladder.Use1V1Mode.Value = true);
            AddAssert("renders DrawableTeamTitleWithHeader", () =>
                this.ChildrenOfType<DrawableTeamTitleWithHeader>().Count(), () => Is.EqualTo(2));
            AddAssert("no DrawableTeamWithPlayers", () =>
                this.ChildrenOfType<DrawableTeamWithPlayers>().Count(), () => Is.EqualTo(0));
        }
    }
}
