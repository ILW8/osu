// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Rulesets;
using osu.Game.Tournament.Models;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tournament.Components
{
    public partial class TournamentBeatmapPanel : CompositeDrawable
    {
        public readonly IBeatmapInfo? Beatmap;

        private readonly string mod;
        private readonly RoundBeatmap? roundBeatmap;

        public const float HEIGHT = 50;

        private readonly Bindable<TournamentMatch?> currentMatch = new Bindable<TournamentMatch?>();

        private Container borderBox = null!;
        private Box flash = null!;
        private TournamentProtectIcon protectIcon = null!;

        [Resolved]
        private IRulesetStore rulesets { get; set; } = null!;

        public TournamentBeatmapPanel(IBeatmapInfo? beatmap, string mod = "")
        {
            Beatmap = beatmap;
            this.mod = mod;

            Width = 400;
            Height = HEIGHT;
        }

        public TournamentBeatmapPanel(RoundBeatmap rb)
        {
            Beatmap = rb.Beatmap;
            roundBeatmap = rb;
            mod = string.Empty;

            Width = 400;
            Height = HEIGHT;
        }

        [BackgroundDependencyLoader]
        private void load(LadderInfo ladder)
        {
            currentMatch.BindValueChanged(matchChanged);
            currentMatch.BindTo(ladder.CurrentMatch);

            Masking = true;

            AddRangeInternal(new Drawable[]
            {
                borderBox = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Black,
                        },
                        new NoUnloadBeatmapSetCover
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = OsuColour.Gray(0.5f),
                            OnlineInfo = (Beatmap as IBeatmapSetOnlineInfo),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Padding = new MarginPadding(15),
                            Direction = FillDirection.Vertical,
                            Children = new Drawable[]
                            {
                                new TournamentSpriteText
                                {
                                    Text = Beatmap?.GetDisplayTitleRomanisable(false, false) ?? (LocalisableString)@"unknown",
                                    Font = OsuFont.Torus.With(weight: FontWeight.Bold),
                                },
                                new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Children = new Drawable[]
                                    {
                                        new TournamentSpriteText
                                        {
                                            Text = "mapper",
                                            Padding = new MarginPadding { Right = 5 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = Beatmap?.Metadata.Author.Username ?? "unknown",
                                            Padding = new MarginPadding { Right = 20 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = "difficulty",
                                            Padding = new MarginPadding { Right = 5 },
                                            Font = OsuFont.Torus.With(weight: FontWeight.Regular, size: 14)
                                        },
                                        new TournamentSpriteText
                                        {
                                            Text = Beatmap?.DifficultyName ?? "unknown",
                                            Font = OsuFont.Torus.With(weight: FontWeight.Bold, size: 14)
                                        },
                                    }
                                }
                            },
                        },
                    },
                },
                protectIcon = new TournamentProtectIcon
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Alpha = 1,
                    Width = Height,
                    Height = Height,
                },
                flash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Gray,
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            });

            if (roundBeatmap != null)
            {
                var rulesetInfo = ladder.Ruleset.Value;
                var ruleset = rulesetInfo == null ? null : rulesets.GetRuleset(rulesetInfo.OnlineID)?.CreateInstance();

                if (ruleset != null)
                {
                    var modFlow = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(2, 0),
                        // Right margin clears the protect-icon wedge (anchored top-right, ~35px
                        // extent along the right edge after the 45° rotation). Matches the
                        // string-path margin below so single- and multi-icon panels line up.
                        Margin = new MarginPadding { Right = 50 },
                    };

                    foreach (var configuredMod in RoundBeatmapModFactory.ConstructMods(roundBeatmap, ruleset))
                    {
                        modFlow.Add(new TournamentModIcon(configuredMod)
                        {
                            // Centre inside the flow cell so icons line up vertically with the
                            // string-path single icon (which uses CentreRight on its own AddInternal).
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            RelativeSizeAxes = Axes.Y,
                            Width = HEIGHT,
                        });
                    }

                    AddInternal(modFlow);
                }
            }
            else if (!string.IsNullOrEmpty(mod))
            {
                AddInternal(new TournamentModIcon(mod)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 50 },
                });
            }
        }

        private void matchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            if (match.OldValue != null)
            {
                match.OldValue.PicksBans.CollectionChanged -= picksBansOnCollectionChanged;
                match.OldValue.Protects.CollectionChanged -= picksBansOnCollectionChanged;
            }
            if (match.NewValue != null)
            {
                match.NewValue.PicksBans.CollectionChanged += picksBansOnCollectionChanged;
                match.NewValue.Protects.CollectionChanged += picksBansOnCollectionChanged;
            }

            Scheduler.AddOnce(updateState);
        }

        private void picksBansOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Scheduler.AddOnce(updateState);

        private BeatmapChoice? choice;

        private void updateState()
        {
            if (currentMatch.Value == null)
            {
                return;
            }

            var protectedChoice = currentMatch.Value.Protects
                .FirstOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);
            protectIcon.TeamColour = protectedChoice?.Team;

            // LastOrDefault so that if a map ends up in PicksBans twice (e.g. operator misclick
            // recovery), the most recent choice is what shows. addForBeatmap (Task 9) only ever
            // allows one PicksBans entry per beatmap, so in normal flow LastOrDefault == FirstOrDefault.
            var newChoice = currentMatch.Value.PicksBans
                .LastOrDefault(p => p.BeatmapID == Beatmap?.OnlineID);

            bool shouldFlash = newChoice != choice;

            if (newChoice != null)
            {
                if (shouldFlash)
                    flash.FadeOutFromOne(500).Loop(0, 10);

                borderBox.BorderThickness = 6;
                borderBox.BorderColour = TournamentGame.GetTeamColour(newChoice.Team);

                switch (newChoice.Type)
                {
                    case ChoiceType.Pick:
                        borderBox.Colour = Color4.White;
                        borderBox.Alpha = 1;
                        break;

                    case ChoiceType.Ban:
                        borderBox.Colour = Color4.Gray;
                        borderBox.Alpha = 0.5f;
                        break;
                }
            }
            else
            {
                borderBox.Colour = Color4.White;
                borderBox.BorderThickness = 0;
                borderBox.Alpha = 1;
            }

            choice = newChoice;
        }

        private partial class NoUnloadBeatmapSetCover : UpdateableOnlineBeatmapSetCover
        {
            // As covers are displayed on stream, we want them to load as soon as possible.
            protected override double LoadDelay => 0;

            // Use DelayedLoadWrapper to avoid content unloading when switching away to another screen.
            protected override DelayedLoadWrapper CreateDelayedLoadWrapper(Func<Drawable> createContentFunc, double timeBeforeLoad)
                => new DelayedLoadWrapper(createContentFunc(), timeBeforeLoad);
        }
    }
}
