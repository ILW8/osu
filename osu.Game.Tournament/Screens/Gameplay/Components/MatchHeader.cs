// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Gameplay.Components
{
    public partial class MatchHeader : Container
    {
        private const int cumulative_delta_height = 24;

        public partial class MatchCumulativeScoreDiffCounter : CommaSeparatedScoreCounter
        {
            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                s.Spacing = new Vector2(-2);
                s.Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: cumulative_delta_height, fixedWidth: true);
            });
        }

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        private TeamScoreDisplay teamDisplay1 = null!;
        private TeamScoreDisplay teamDisplay2 = null!;
        private DrawableTournamentHeaderLogo logo = null!;
        private MatchRoundDisplay roundDisplay = null!;
        private MatchCumulativeScoreDiffCounter cumulativeScoreDiffCounter = null!;
        private FillFlowContainer cumulativeScoreDiffCounterContainer = null!;
        private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();
        private readonly BindableDictionary<string, Tuple<long, long>> matchScores = new BindableDictionary<string, Tuple<long, long>>();
        private Bindable<bool> useCumulativeScore = null!;
        private SpriteIcon leftWinningTriangle = null!;
        private SpriteIcon rightWinningTriangle = null!;

        private bool showScores = true;

        public bool ShowScores
        {
            get => showScores;
            set
            {
                if (value == showScores)
                    return;

                showScores = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        private bool showLogo = true;

        public bool ShowLogo
        {
            get => showLogo;
            set
            {
                if (value == showLogo)
                    return;

                showLogo = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        private bool showMatchRound = true;

        /// <summary>
        /// Whether to render the round-name caption (e.g. "Quarterfinals") above the
        /// cumulative-score counter. LGA 2025 hides this on the gameplay overlay and the
        /// map-pool screen — the round name is already conveyed by team intro / setup.
        /// </summary>
        public bool ShowMatchRound
        {
            get => showMatchRound;
            set
            {
                if (value == showMatchRound)
                    return;

                showMatchRound = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            useCumulativeScore = ladder.CumulativeScore.GetBoundCopy();
            currentMatch.BindTo(ladder.CurrentMatch);

            RelativeSizeAxes = Axes.X;
            Height = 95;
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(20),
                    Spacing = new Vector2(5),
                    Children = new Drawable[]
                    {
                        logo = new DrawableTournamentHeaderLogo
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Alpha = showLogo ? 1 : 0
                        },
                        new DrawableTournamentHeaderText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                        },
                        roundDisplay = new MatchRoundDisplay
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Scale = new Vector2(0.4f)
                        },
                        cumulativeScoreDiffCounterContainer = new FillFlowContainer
                        {
                            Direction = FillDirection.Horizontal,
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Spacing = new Vector2(16),
                            Margin = new MarginPadding { Top = 16 },
                            Children = new Drawable[]
                            {
                                leftWinningTriangle = new SpriteIcon
                                {
                                    Icon = FontAwesome.Solid.ChevronLeft,
                                    Width = 12,
                                    Height = 12,
                                    Margin = new MarginPadding { Vertical = 7 },
                                    Alpha = 0,
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AlwaysPresent = true
                                },
                                cumulativeScoreDiffCounter = new MatchCumulativeScoreDiffCounter
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                },
                                rightWinningTriangle = new SpriteIcon
                                {
                                    Icon = FontAwesome.Solid.ChevronRight,
                                    Width = 12,
                                    Height = 12,
                                    Margin = new MarginPadding { Vertical = 7 },
                                    Alpha = 0,
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AlwaysPresent = true
                                },
                            }
                        }
                    }
                },
                teamDisplay1 = new TeamScoreDisplay(TeamColour.Red)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                },
                teamDisplay2 = new TeamScoreDisplay(TeamColour.Blue)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
            };
        }

        private void updateScoreDelta(string trigger)
        {
            if (currentMatch.Value == null)
            {
                Logger.Log($"[MatchHeader] updateScoreDelta({trigger}) skipped — currentMatch=null");
                return;
            }

            long scoreDelta = calculateScoreDelta();

            Logger.Log($"[MatchHeader] updateScoreDelta({trigger}) delta={scoreDelta} → counter={Math.Abs(scoreDelta)}");

            cumulativeScoreDiffCounter.Current.Value = Math.Abs(scoreDelta);

            leftWinningTriangle.FadeTo(scoreDelta > 0 ? 1 : 0, 200);
            rightWinningTriangle.FadeTo(scoreDelta < 0 ? 1 : 0, 200);
            return;

            long calculateScoreDelta()
            {
                if (currentMatch.Value == null)
                    return 0;

                // "Current set" = the set the latest pick landed into, i.e. the last entry in Sets.
                // Intentionally not tied to ipc.Beatmap: a stream operator may need to override
                // sets manually after an accidental room map change, and the header should reflect
                // their picks rather than what the room happens to be showing.
                var set = currentMatch.Value.Sets.LastOrDefault();
                var scores = set?.GetSetScores(currentMatch.Value);

                Logger.Log($"[MatchHeader]   calculateScoreDelta: lastSet={(set == null ? "null" : "set")} " +
                           $"setMaps=[{set?.Map1Id.Value ?? -1},{set?.Map2Id.Value ?? -1},{set?.Map3Id.Value ?? -1}] " +
                           $"scores={(scores == null ? "null" : $"({scores.Item1},{scores.Item2})")} " +
                           $"mapScoresKeys=[{string.Join(",", currentMatch.Value.MapScores.Keys)}]");

                return scores != null ? scores.Item1 - scores.Item2 : 0;
            }
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            Logger.Log($"[MatchHeader] matchChanged: old={(match.OldValue == null ? "null" : "set")} new={(match.NewValue == null ? "null" : "set")}");

            if (match.OldValue != null)
            {
                match.OldValue.PicksBans.CollectionChanged -= onPicksBansChanged;
                match.OldValue.Sets.CollectionChanged -= onSetsChanged;
            }

            matchScores.UnbindBindings();

            if (match.NewValue != null)
            {
                matchScores.BindTo(match.NewValue.MapScores);
                match.NewValue.PicksBans.CollectionChanged += onPicksBansChanged;
                match.NewValue.Sets.CollectionChanged += onSetsChanged;
            }

            // Defer so that MapPoolScreen.updateSets has finished writing slot bindables before
            // we read Sets.LastOrDefault().
            Scheduler.AddOnce(() => updateScoreDelta("matchChanged"));
        }

        private void onPicksBansChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Logger.Log($"[MatchHeader] PicksBans changed: action={e.Action}");

            // PicksBans.CollectionChanged fires synchronously from PicksBans.Add/Remove, BEFORE
            // MapPoolScreen.updateSets runs on the next line — so Sets is still stale at this
            // moment. Defer the recompute to the next frame so we read post-updateSets state.
            // Also covers pick-within-existing-set (Map2 of a half-full set) which doesn't fire
            // Sets.CollectionChanged because no new MatchSet was added.
            Scheduler.AddOnce(() => updateScoreDelta($"PicksBans.{e.Action}"));
        }

        private void onSetsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Logger.Log($"[MatchHeader] Sets changed: action={e.Action}");
            Scheduler.AddOnce(() => updateScoreDelta($"Sets.{e.Action}"));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentMatch.BindValueChanged(matchChanged, true);
            useCumulativeScore.BindValueChanged(_ => updateDisplay(), true);
            matchScores.BindCollectionChanged((_, args) =>
            {
                Logger.Log($"[MatchHeader] matchScores changed: action={args.Action}");
                updateScoreDelta("matchScores");
            });

            updateDisplay();
        }

        private void updateDisplay()
        {
            teamDisplay1.ShowScore = showScores;
            teamDisplay2.ShowScore = showScores;
            cumulativeScoreDiffCounterContainer.FadeTo(showScores && useCumulativeScore.Value ? 1 : 0, 200);

            logo.Alpha = showLogo ? 1 : 0;
            roundDisplay.Alpha = showMatchRound ? 1 : 0;
        }
    }
}
