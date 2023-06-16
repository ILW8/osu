// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Tournament.Screens.MapPool
{
    // ReSharper disable once PartialTypeWithSinglePart
    public partial class MapPoolScreen : TournamentMatchScreen
    {
        private readonly FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>> mapFlows;
        private readonly FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>> mapFlowsCol2;

        [Resolved(canBeNull: true)]
        private TournamentSceneManager sceneManager { get; set; }

        private TeamColour pickColour;
        private ChoiceType pickType;

        private readonly OsuButton buttonRedBan;
        private readonly OsuButton buttonBlueBan;
        private readonly OsuButton buttonRedPick;
        private readonly OsuButton buttonBluePick;

        private readonly MatchHeader matchHeader;

        public MapPoolScreen()
        {
            InternalChildren = new Drawable[]
            {
                new TourneyVideo("mappool")
                {
                    Loop = true,
                    RelativeSizeAxes = Axes.Both,
                },
                matchHeader = new MatchHeader
                {
                    ShowScores = true,
                },
                new FillFlowContainer<FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>>
                {
                    Y = 136,
                    X = 0,
                    Direction = FillDirection.Horizontal,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new[]
                    {
                        mapFlows = new FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>
                        {
                            Width = 0.5f,
                            Spacing = new Vector2(0, 4),
                            Direction = FillDirection.Vertical,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                        },
                        mapFlowsCol2 = new FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>
                        {
                            Width = 0.5f,
                            Spacing = new Vector2(0, 4),
                            Direction = FillDirection.Vertical,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                        },
                    }
                },
                new ControlPanel
                {
                    Children = new Drawable[]
                    {
                        new TournamentSpriteText
                        {
                            Text = "Current Mode"
                        },
                        buttonRedBan = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Red Ban",
                            Action = () => setMode(TeamColour.Red, ChoiceType.Ban)
                        },
                        buttonBlueBan = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Blue Ban",
                            Action = () => setMode(TeamColour.Blue, ChoiceType.Ban)
                        },
                        buttonRedPick = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Red Pick",
                            Action = () => setMode(TeamColour.Red, ChoiceType.Pick)
                        },
                        buttonBluePick = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Blue Pick",
                            Action = () => setMode(TeamColour.Blue, ChoiceType.Pick)
                        },
                        new ControlPanel.Spacer(),
                        new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Reset",
                            Action = reset
                        },
                        new ControlPanel.Spacer(),
                    },
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            ipc.Beatmap.BindValueChanged(beatmapChanged);
        }

        private void beatmapChanged(ValueChangedEvent<TournamentBeatmap> beatmap)
        {
            if (CurrentMatch.Value == null || CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) < 2)
                return;

            // if bans have already been placed, beatmap changes result in a selection being made autoamtically
            if (beatmap.NewValue.OnlineID > 0)
                addForBeatmap(beatmap.NewValue.OnlineID);
        }

        private void setMode(TeamColour colour, ChoiceType choiceType)
        {
            pickColour = colour;
            pickType = choiceType;

            static Color4 setColour(bool active) => active ? Color4.White : Color4.Gray;

            buttonRedBan.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Ban);
            buttonBlueBan.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Ban);
            buttonRedPick.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Pick);
            buttonBluePick.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Pick);
        }

        private void setNextMode()
        {
            const TeamColour roll_winner = TeamColour.Red; //todo: draw from match

            var nextColour = (CurrentMatch.Value.PicksBans.LastOrDefault()?.Team ?? roll_winner) == TeamColour.Red ? TeamColour.Blue : TeamColour.Red;

            if (pickType == ChoiceType.Ban && CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) >= 2)
                setMode(pickColour, ChoiceType.Pick);
            else
                setMode(nextColour, CurrentMatch.Value.PicksBans.Count(p => p.Type == ChoiceType.Ban) >= 2 ? ChoiceType.Pick : ChoiceType.Ban);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            var maps = mapFlows.Select(f => f.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)));
            var map = maps.FirstOrDefault(m => m != null);

            if (map == null)
            {
                maps = mapFlowsCol2.Select(f => f.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)));
                map = maps.FirstOrDefault(m => m != null);
            }

            if (map == null) return base.OnMouseDown(e);

            if (e.Button == MouseButton.Left && map.Beatmap.OnlineID > 0)
                addForBeatmap(map.Beatmap.OnlineID);
            else
            {
                var existing = CurrentMatch.Value.PicksBans.FirstOrDefault(p => p.BeatmapID == map.Beatmap.OnlineID);

                if (existing == null) return true;

                CurrentMatch.Value.PicksBans.Remove(existing);
                setNextMode();
            }

            return true;
        }

        private void reset()
        {
            CurrentMatch.Value.PicksBans.Clear();
            setNextMode();
        }

        private ScheduledDelegate scheduledChange;

        private void addForBeatmap(int beatmapId)
        {
            if (CurrentMatch.Value == null)
                return;

            if (CurrentMatch.Value.Round.Value.Beatmaps.All(b => b.Beatmap.OnlineID != beatmapId) &&
                CurrentMatch.Value.Round.Value.Beatmaps2.All(b => b.Beatmap.OnlineID != beatmapId))
                // don't attempt to add if the beatmap isn't in our pool
                return;

            if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId))
                // don't attempt to add if already exists.
                return;

            CurrentMatch.Value.PicksBans.Add(new BeatmapChoice
            {
                Team = pickColour,
                Type = pickType,
                BeatmapID = beatmapId
            });

            setNextMode();

            if (!LadderInfo.AutoProgressScreens.Value) return;

            if (pickType != ChoiceType.Pick || CurrentMatch.Value.PicksBans.All(i => i.Type != ChoiceType.Pick)) return;

            scheduledChange?.Cancel();
            scheduledChange = Scheduler.AddDelayed(() => { sceneManager?.SetScreen(typeof(GameplayScreen)); }, 10000);
        }

        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch> match)
        {
            base.CurrentMatchChanged(match);

            mapFlows.Clear();
            mapFlowsCol2.Clear();

            if (match.NewValue == null)
                return;

            int totalRows = 0;

            if (match.NewValue.Round.Value != null)
            {
                FillFlowContainer<TournamentBeatmapPanel> currentFlow = null;
                string currentMod = null;

                int flowCount = 0;
                int panelWidth = 400;
                int panelHeight = 42;
                bool tall = false;

                if (match.NewValue.Round.Value.Beatmaps2.Count == 0) // no 2nd pool
                {
                    mapFlows.Width = 1.0f;
                    mapFlows.Margin = new MarginPadding { Top = 24 };
                }
                else
                {
                    mapFlows.Width = 0.5f;
                    panelWidth = 224;
                    panelHeight = 56;
                    tall = true;
                    mapFlows.Margin = new MarginPadding(0);
                    matchHeader.MatchRoundDisplay.Margin = new MarginPadding { Top = -115 };
                }

                foreach (var b in match.NewValue.Round.Value.Beatmaps)
                {
                    if (currentFlow == null || currentMod != b.Mods)
                    {
                        mapFlows.Add(currentFlow = new FillFlowContainer<TournamentBeatmapPanel>
                        {
                            Spacing = new Vector2(2, 2),
                            Direction = FillDirection.Full,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y
                        });

                        currentMod = b.Mods;

                        totalRows++;
                        flowCount = 0;
                    }

                    if (++flowCount > 2)
                    {
                        totalRows++;
                        flowCount = 1;
                    }

                    currentFlow.Add(new TournamentBeatmapPanel(b.Beatmap, b.Mods, tall: tall)
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Height = panelHeight,
                        Width = panelWidth
                    });
                }

                FillFlowContainer<TournamentBeatmapPanel> currentFlow2 = null;
                currentMod = null;

                foreach (var b in match.NewValue.Round.Value.Beatmaps2)
                {
                    if (currentFlow2 == null || currentMod != b.Mods)
                    {
                        mapFlowsCol2.Add(currentFlow2 = new FillFlowContainer<TournamentBeatmapPanel>
                        {
                            Spacing = new Vector2(2, 2),
                            Direction = FillDirection.Full,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y
                        });

                        currentMod = b.Mods;

                        totalRows++;
                        flowCount = 0;
                    }

                    if (++flowCount > 2)
                    {
                        totalRows++;
                        flowCount = 1;
                    }

                    currentFlow2.Add(new TournamentBeatmapPanel(b.Beatmap, b.Mods, tall: tall)
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Height = panelHeight,
                        Width = panelWidth
                    });
                }
            }

            mapFlows.Padding = new MarginPadding(5)
            {
                // remove horizontal padding to increase flow width to 3 panels
                Horizontal = totalRows > 9 ? 0 : 50
            };
            mapFlowsCol2.Padding = new MarginPadding(5)
            {
                // remove horizontal padding to increase flow width to 3 panels
                Horizontal = totalRows > 9 ? 0 : 50
            };
        }
    }
}
