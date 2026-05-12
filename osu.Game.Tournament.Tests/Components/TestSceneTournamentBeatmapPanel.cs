// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Visual;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.Tests.Components
{
    public partial class TestSceneTournamentBeatmapPanel : TournamentTestScene
    {
        /// <remarks>
        /// Warning: the below API instance is actually the online API, rather than the dummy API provided by the test.
        /// It cannot be trivially replaced because setting <see cref="OsuTestScene.UseOnlineAPI"/> to <see langword="true"/> causes <see cref="OsuTestScene.API"/> to no longer be usable.
        /// </remarks>
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private TournamentBeatmapPanel panel = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            var req = new GetBeatmapRequest(new APIBeatmap { OnlineID = 1091460 });
            req.Success += success;
            api.Queue(req);
        }

        private void success(APIBeatmap beatmap)
        {
            Add(panel = new TournamentBeatmapPanel(new TournamentBeatmap(beatmap))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });
        }

        [Test]
        public void TestProtectIconRender()
        {
            AddUntilStep("panel ready", () => panel != null && panel.IsLoaded);

            AddStep("set red protect", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
                {
                    Team = TeamColour.Red,
                    Type = ChoiceType.Protect,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("protect icon visible", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().Any(i => i.Alpha == 1f && i.TeamColour == TeamColour.Red));

            AddStep("switch to blue protect", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.Protects.Add(new BeatmapChoice
                {
                    Team = TeamColour.Blue,
                    Type = ChoiceType.Protect,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("protect icon tinted blue", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().Any(i => i.Alpha == 1f && i.TeamColour == TeamColour.Blue));

            AddStep("ban same map", () =>
            {
                Ladder.CurrentMatch.Value!.PicksBans.Add(new BeatmapChoice
                {
                    Team = TeamColour.Red,
                    Type = ChoiceType.Ban,
                    BeatmapID = panel.Beatmap!.OnlineID,
                });
            });
            AddUntilStep("ban dim does not affect protect icon",
                () => panel.ChildrenOfType<TournamentProtectIcon>().First().Alpha == 1f);

            AddStep("clear", () =>
            {
                Ladder.CurrentMatch.Value!.Protects.Clear();
                Ladder.CurrentMatch.Value!.PicksBans.Clear();
            });
            AddUntilStep("protect icon hidden", () =>
                panel.ChildrenOfType<TournamentProtectIcon>().All(i => i.Alpha == 0f));
        }
    }
}
