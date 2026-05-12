// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Graphics;
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
    public partial class MapPoolScreen : TournamentMatchScreen
    {
        public int TiebreakerSetIndex { get; set; } = -1;

        private FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>> mapFlows = null!;
        private FillFlowContainer<TournamentSetPanel> setsFlow = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        private TeamColour pickColour;
        private ChoiceType pickType;

        private OsuButton buttonRedBan = null!;
        private OsuButton buttonBlueBan = null!;
        private OsuButton buttonRedPick = null!;
        private OsuButton buttonBluePick = null!;
        private OsuButton buttonRedProtect = null!;
        private OsuButton buttonBlueProtect = null!;

        private ScheduledDelegate? scheduledScreenChange;

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            InternalChildren = new Drawable[]
            {
                new TourneyVideo("mappool")
                {
                    Loop = true,
                    RelativeSizeAxes = Axes.Both,
                },
                new MatchHeader
                {
                    ShowScores = true,
                },
                new GridContainer
                {
                    // Y/X/Width values verbatim from 2025.524.2-LGA+2025.424.0-week2 — the asymmetric
                    // Y=90 (Pool) vs Y=170 (Sets) is intentional: 90 puts the Pool heading at the
                    // existing 90–160 band so mapFlows resumes at ~Y=160 (matches the pre-split layout
                    // and keeps updateDisplay's padding logic valid); 170 clears MatchHeader for Sets.
                    Y = 90,
                    X = 0f,
                    Anchor = Anchor.TopLeft,
                    RelativePositionAxes = Axes.X,
                    Width = 0.65f,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    // TODO: verbatim port from 2025 LGA tag — Content has 2 rows (heading + flow) but
                    // RowDimensions has only 1 entry. Verify behaviour at runtime: osu-framework may
                    // pad missing entries with Distributed (which would conflict with AutoSizeAxes.Y),
                    // or it may tolerate the mismatch. If broken, add a second `new Dimension(GridSizeMode.AutoSize)`.
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Padding = new MarginPadding { Vertical = 4 },
                                Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 18),
                                Text = "Pool",
                            },
                        },
                        new Drawable[]
                        {
                            mapFlows = new FillFlowContainer<FillFlowContainer<TournamentBeatmapPanel>>
                            {
                                Anchor = Anchor.TopLeft,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Spacing = new Vector2(10, 10),
                                Direction = FillDirection.Vertical,
                            },
                        },
                    },
                },
                new GridContainer
                {
                    // Y=170 verbatim from 2025 LGA tag (clears MatchHeader for the Sets column).
                    Y = 170,
                    X = 0.65f,
                    Anchor = Anchor.TopLeft,
                    RelativePositionAxes = Axes.X,
                    Width = 0.35f,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    // TODO: same mismatch as Pool grid above — verify osu-framework behaviour at runtime.
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new TournamentSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Padding = new MarginPadding { Vertical = 4 },
                                Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 18),
                                Text = "Sets",
                            },
                        },
                        new Drawable[]
                        {
                            setsFlow = new FillFlowContainer<TournamentSetPanel>
                            {
                                Anchor = Anchor.TopLeft,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Spacing = new Vector2(10, 5),
                                Direction = FillDirection.Full,
                            },
                        },
                    },
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
                        buttonRedProtect = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Red Protect",
                            Action = () => setMode(TeamColour.Red, ChoiceType.Protect)
                        },
                        buttonBlueProtect = new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Blue Protect",
                            Action = () => setMode(TeamColour.Blue, ChoiceType.Protect)
                        },
                        new ControlPanel.Spacer(),
                        new TourneyButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Text = "Reset",
                            Action = reset
                        },
                        new ControlPanel.Spacer(),
                        new OsuCheckbox
                        {
                            LabelText = "Split display by mods",
                            Current = LadderInfo.SplitMapPoolByMods,
                        },
                    },
                }
            };

            ipc.Beatmap.BindValueChanged(beatmapChanged);
        }

        private Bindable<bool>? splitMapPoolByMods;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            splitMapPoolByMods = LadderInfo.SplitMapPoolByMods.GetBoundCopy();
            splitMapPoolByMods.BindValueChanged(_ => updateDisplay());
        }

        private void beatmapChanged(ValueChangedEvent<TournamentBeatmap?> beatmap)
        {
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            int draftIndex = CurrentMatch.Value.PicksBans.Count + CurrentMatch.Value.Protects.Count;

            // Auto-add on beatmap-change only kicks in once the draft has reached the pick phase
            // (LGA: index ≥ 6, i.e. all 4 bans + 2 protects placed).
            if (draftIndex < 6)
                return;

            if (beatmap.NewValue?.OnlineID > 0)
                addForBeatmap(beatmap.NewValue.OnlineID);
        }

        private void setMode(TeamColour colour, ChoiceType choiceType)
        {
            pickColour = colour;
            pickType = choiceType;

            buttonRedBan.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Ban);
            buttonBlueBan.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Ban);
            buttonRedPick.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Pick);
            buttonBluePick.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Pick);
            buttonRedProtect.Colour = setColour(pickColour == TeamColour.Red && pickType == ChoiceType.Protect);
            buttonBlueProtect.Colour = setColour(pickColour == TeamColour.Blue && pickType == ChoiceType.Protect);

            static Color4 setColour(bool active) => active ? Color4.White : Color4.Gray;
        }

        // LGA 2026 §3.4–§3.5 draft order: 2 bans (LS, HS), 2 protects (LS, HS), 2 bans (LS, HS),
        // then 10 ABBA picks across 5 sets × 2 maps starting with HS (where A=High Seed=Red, B=Low Seed=Blue).
        // Team mapping (see room-name parser, commit 5e2a7cbb): Team1 = Red = High Seed (HS),
        // Team2 = Blue = Low Seed (LS).
        //
        // These arrays are size 16 (6 bans+protects + 10 picks). If a non-LGA round on this branch
        // has BestOf or pool size that would extend the draft beyond 16, setNextMode no-ops past
        // index 16 — acceptable since the branch ships LGA only.
        private static readonly ChoiceType[] map_operation_order =
        {
            ChoiceType.Ban, ChoiceType.Ban,
            ChoiceType.Protect, ChoiceType.Protect,
            ChoiceType.Ban, ChoiceType.Ban,
            ChoiceType.Pick, ChoiceType.Pick,
            ChoiceType.Pick, ChoiceType.Pick,
            ChoiceType.Pick, ChoiceType.Pick,
            ChoiceType.Pick, ChoiceType.Pick,
            ChoiceType.Pick, ChoiceType.Pick,
        };

        private static readonly TeamColour[] team_colour_order =
        {
            TeamColour.Blue, TeamColour.Red, // ban
            TeamColour.Blue, TeamColour.Red, // protect
            TeamColour.Blue, TeamColour.Red, // ban
            TeamColour.Red,  TeamColour.Blue,
            TeamColour.Blue, TeamColour.Red,
            TeamColour.Red,  TeamColour.Blue,
            TeamColour.Blue, TeamColour.Red,
            TeamColour.Red,  TeamColour.Blue,
        };

        private void setNextMode()
        {
            if (CurrentMatch.Value == null)
                return;

            int index = CurrentMatch.Value.PicksBans.Count + CurrentMatch.Value.Protects.Count;

            if (index >= map_operation_order.Length)
                return; // draft is over — leave mode at last value

            setMode(team_colour_order[index], map_operation_order[index]);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            var maps = mapFlows.Select(f => f.FirstOrDefault(m => m.ReceivePositionalInputAt(e.ScreenSpaceMousePosition)));
            var map = maps.FirstOrDefault(m => m != null);

            if (map != null)
            {
                if (e.Button == MouseButton.Left && map.Beatmap?.OnlineID > 0)
                {
                    addForBeatmap(map.Beatmap.OnlineID);
                }
                else
                {
                    // Two-stage removal: prefer removing a Pick or Ban first; if none, fall back to removing a Protect.
                    var existing = CurrentMatch.Value?.PicksBans
                        .FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID
                                             && (p.Type == ChoiceType.Pick || p.Type == ChoiceType.Ban));

                    if (existing != null)
                    {
                        CurrentMatch.Value?.PicksBans.Remove(existing);
                    }
                    else
                    {
                        var existingProtect = CurrentMatch.Value?.Protects
                            .FirstOrDefault(p => p.BeatmapID == map.Beatmap?.OnlineID);

                        if (existingProtect == null)
                            return true;

                        CurrentMatch.Value?.Protects.Remove(existingProtect);
                    }

                    updateSets();
                    updateSetsDisplay();
                    setNextMode();
                }

                return true;
            }

            return base.OnMouseDown(e);
        }

        private void reset()
        {
            CurrentMatch.Value?.PicksBans.Clear();
            CurrentMatch.Value?.Protects.Clear();
            CurrentMatch.Value?.Sets.Clear();
            updateSetsDisplay();
            setNextMode();
        }

        private void updateSetsDisplay()
        {
            if (CurrentMatch.Value == null)
            {
                setsFlow.Clear();
                return;
            }

            var sets = CurrentMatch.Value.Sets;

            // sets get rebuilt from scratch on shrinkage; the panels reference specific MatchSet instances
            // so a full rebuild is the simplest correct approach when count changes.
            if (setsFlow.Count > sets.Count)
                setsFlow.Clear();

            while (setsFlow.Count < sets.Count)
            {
                var currentSet = sets[setsFlow.Count];
                setsFlow.Add(new TournamentSetPanel(currentSet)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Height = currentSet.IsTiebreaker ? 72 : 48,
                });
            }
        }

        /// <summary>
        /// Synchronises <see cref="TournamentMatch.Sets"/> with the picks recorded in <see cref="TournamentMatch.PicksBans"/>.
        /// Each consecutive pair of picks forms a regular set; once <see cref="TiebreakerSetIndex"/> is reached, additional picks
        /// are placed onto the slots of that tiebreaker set (which holds up to three maps).
        /// </summary>
        private void updateSets()
        {
            if (CurrentMatch.Value == null)
                return;

            var picks = CurrentMatch.Value.PicksBans.Where(pb => pb.Type == ChoiceType.Pick).ToList();
            var sets = CurrentMatch.Value.Sets;

            for (int pickIndex = 0; pickIndex < picks.Count; pickIndex++)
            {
                int setIndex = TiebreakerSetIndex >= 0 ? Math.Min(pickIndex / 2, TiebreakerSetIndex) : pickIndex / 2;
                int setSlot = pickIndex % 2;

                MatchSet currentSet;

                if (sets.Count - 1 < setIndex)
                {
                    sets.Add(currentSet = new MatchSet(TiebreakerSetIndex >= 0 && setIndex == TiebreakerSetIndex));
                }
                else
                {
                    currentSet = sets[setIndex];
                }

                BindableLong setSlotBindable = currentSet.IsTiebreaker && pickIndex == (TiebreakerSetIndex + 1) * 2
                    ? currentSet.Map3Id
                    : setSlot == 0
                        ? currentSet.Map1Id
                        : currentSet.Map2Id;

                if (setSlotBindable.Value != picks[pickIndex].BeatmapID)
                    setSlotBindable.Value = picks[pickIndex].BeatmapID;
            }

            // clear stale Map2 slot if the last (non-tiebreaker) set only has one pick.
            var lastSet = sets.LastOrDefault();

            if (lastSet != null && !lastSet.IsTiebreaker && picks.Count % 2 == 1)
                lastSet.Map2Id.Value = 0;

            int expectedSets = (picks.Count + 1) / 2;

            while (sets.Count > expectedSets)
                sets.RemoveAt(sets.Count - 1);
        }

        private void addForBeatmap(int beatmapId)
        {
            if (CurrentMatch.Value?.Round.Value == null)
                return;

            if (CurrentMatch.Value.Round.Value.Beatmaps.All(b => b.Beatmap?.OnlineID != beatmapId))
                // don't attempt to add if the beatmap isn't in our pool
                return;

            var existingProtect = CurrentMatch.Value.Protects
                .FirstOrDefault(p => p.BeatmapID == beatmapId);

            bool alreadyHandled = existingProtect != null
                                  || CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId);

            if (alreadyHandled)
            {
                // Re-protecting an already-protected map is a no-op (covers operator misclicks during
                // live ops; the LGA draft order normally advances past Protect mode after 2 protects,
                // but the Protect buttons remain clickable).
                if (existingProtect != null && pickType == ChoiceType.Protect)
                    return;

                // Map already in some state. The only legal follow-up is a pick of a protected map —
                // and that pick may be by either team or only by the protector, depending on
                // AllowPickingOpponentProtects.
                bool allowPick = existingProtect != null;

                if (!CurrentMatch.Value.Round.Value.AllowPickingOpponentProtects.Value)
                {
                    if (pickType != ChoiceType.Pick || pickColour != existingProtect?.Team)
                        allowPick = false;
                }

                // Already picked after protect → reject (one pick per map, even protected ones).
                if (CurrentMatch.Value.PicksBans.Any(p => p.BeatmapID == beatmapId
                                                          && p.Type == ChoiceType.Pick))
                    allowPick = false;

                if (!allowPick)
                    return;
            }

            if (pickType == ChoiceType.Protect)
            {
                CurrentMatch.Value.Protects.Add(new BeatmapChoice
                {
                    Team = pickColour,
                    Type = pickType,
                    BeatmapID = beatmapId,
                });
            }
            else
            {
                CurrentMatch.Value.PicksBans.Add(new BeatmapChoice
                {
                    Team = pickColour,
                    Type = pickType,
                    BeatmapID = beatmapId,
                });
            }

            updateSets();
            updateSetsDisplay();

            setNextMode();

            if (LadderInfo.AutoProgressScreens.Value)
            {
                if (pickType == ChoiceType.Pick && CurrentMatch.Value.PicksBans.Any(i => i.Type == ChoiceType.Pick))
                {
                    scheduledScreenChange?.Cancel();
                    scheduledScreenChange = Scheduler.AddDelayed(() => { sceneManager?.SetScreen(typeof(GameplayScreen)); }, 10000);
                }
            }
        }

        public override void Hide()
        {
            scheduledScreenChange?.Cancel();
            base.Hide();
        }

        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);
            updateDisplay();
            updateSetsDisplay();
        }

        private void updateDisplay()
        {
            mapFlows.Clear();

            if (CurrentMatch.Value == null)
                return;

            int totalRows = 0;

            if (CurrentMatch.Value.Round.Value != null)
            {
                FillFlowContainer<TournamentBeatmapPanel>? currentFlow = null;
                string? currentMods = null;
                int flowCount = 0;

                foreach (var b in CurrentMatch.Value.Round.Value.Beatmaps)
                {
                    if (currentFlow == null || (LadderInfo.SplitMapPoolByMods.Value && currentMods != b.Mods))
                    {
                        mapFlows.Add(currentFlow = new FillFlowContainer<TournamentBeatmapPanel>
                        {
                            Spacing = new Vector2(10, 5),
                            Direction = FillDirection.Full,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y
                        });

                        currentMods = b.Mods;

                        totalRows++;
                        flowCount = 0;
                    }

                    if (++flowCount > 2)
                    {
                        totalRows++;
                        flowCount = 1;
                    }

                    currentFlow.Add(new TournamentBeatmapPanel(b)
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Height = 42,
                    });
                }
            }

            mapFlows.Padding = new MarginPadding(5)
            {
                // Padding halved (was 100) to track the narrower 65% Pool column; row-count boundary
                // kept at `> 9` so TestJustEnoughMaps/TestJustEnoughMods (the empirical anchors) still
                // describe the 2-wide → 3-wide flip. Spec §7.2 proposed `> 7` as a starting estimate.
                Horizontal = totalRows > 9 ? 0 : 50
            };
        }
    }
}
