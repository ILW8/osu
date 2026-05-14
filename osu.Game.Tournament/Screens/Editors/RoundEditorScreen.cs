// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Editors.Components;
using osuTK;

namespace osu.Game.Tournament.Screens.Editors
{
    public partial class RoundEditorScreen : TournamentEditorScreen<RoundEditorScreen.RoundRow, TournamentRound>
    {
        protected override BindableList<TournamentRound> Storage => LadderInfo.Rounds;

        public partial class RoundRow : CompositeDrawable, IModelBacked<TournamentRound>
        {
            public TournamentRound Model { get; }

            [Resolved]
            private LadderInfo ladderInfo { get; set; } = null!;

            [Resolved]
            private IDialogOverlay? dialogOverlay { get; set; }

            public RoundRow(TournamentRound round)
            {
                Model = round;

                Masking = true;
                CornerRadius = 10;

                RoundBeatmapEditor beatmapEditor = new RoundBeatmapEditor(round)
                {
                    Width = 0.95f
                };

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        Colour = OsuColour.Gray(0.1f),
                        RelativeSizeAxes = Axes.Both,
                    },
                    new FillFlowContainer
                    {
                        Margin = new MarginPadding(5),
                        Padding = new MarginPadding { Right = 160 },
                        Spacing = new Vector2(5),
                        Direction = FillDirection.Full,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            new SettingsTextBox
                            {
                                LabelText = "Name",
                                Width = 0.33f,
                                Current = Model.Name
                            },
                            new SettingsTextBox
                            {
                                LabelText = "Description",
                                Width = 0.33f,
                                Current = Model.Description
                            },
                            new DateTextBox
                            {
                                LabelText = "Start Time",
                                Width = 0.33f,
                                Current = Model.StartDate
                            },
                            new SettingsSlider<int>
                            {
                                LabelText = "# of Bans",
                                Width = 0.24f,
                                Current = Model.BanCount
                            },
                            new SettingsSlider<int>
                            {
                                LabelText = "# of Protects",
                                Width = 0.24f,
                                Current = Model.ProtectCount
                            },
                            new SettingsCheckbox
                            {
                                LabelText = "Allow picking opponent's protects",
                                Width = 0.24f,
                                Current = Model.AllowPickingOpponentProtects
                            },
                            new SettingsSlider<int>
                            {
                                LabelText = "Best of",
                                Width = 0.24f,
                                Current = Model.BestOf
                            },
                            new SettingsButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Margin = new MarginPadding(10),
                                Text = "Add beatmap",
                                Action = beatmapEditor.CreateNew
                            },
                            beatmapEditor
                        }
                    },
                    new DangerousSettingsButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        RelativeSizeAxes = Axes.None,
                        Width = 150,
                        Text = "Delete Round",
                        Action = () => dialogOverlay?.Push(new DeleteRoundDialog(Model, () =>
                        {
                            Expire();
                            ladderInfo.Rounds.Remove(Model);
                        }))
                    }
                };

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Auto-compute SlotName (NM1, NM2, HD1, ...) from each beatmap's Mods field.
                // Ported verbatim from LGA 2025 commit 16888f90d6; the field-only port at
                // 361c48cb84 left this hook out, which silently broke MapPoolScreen's score-edit
                // slot dropdown (empties get filtered by refreshSlotItems).
                // BindCollectionChanged with immediate=true also fires on screen load, so existing
                // bracket.json rounds get their SlotNames materialised the first time the round
                // editor is opened.
                Model.Beatmaps.BindCollectionChanged((_, _) =>
                {
                    string? currentMods = null;
                    int modSlotIndex = 1;

                    foreach (var b in Model.Beatmaps)
                    {
                        if (currentMods != b.Mods)
                        {
                            currentMods = b.Mods;
                            modSlotIndex = 1;
                        }

                        b.SlotName = currentMods == "TB" ? currentMods : $"{currentMods}{modSlotIndex}";
                        modSlotIndex++;
                    }
                }, true);
            }

            public partial class RoundBeatmapEditor : CompositeDrawable
            {
                private readonly TournamentRound round;
                private readonly FillFlowContainer flow;

                public RoundBeatmapEditor(TournamentRound round)
                {
                    this.round = round;

                    RelativeSizeAxes = Axes.X;
                    AutoSizeAxes = Axes.Y;

                    InternalChild = flow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        ChildrenEnumerable = round.Beatmaps.Select(p => new RoundBeatmapRow(round, p))
                    };
                }

                public void CreateNew()
                {
                    var b = new RoundBeatmap();

                    round.Beatmaps.Add(b);

                    flow.Add(new RoundBeatmapRow(round, b));
                }

                public partial class RoundBeatmapRow : CompositeDrawable
                {
                    public RoundBeatmap Model { get; }

                    [Resolved]
                    protected IAPIProvider API { get; private set; } = null!;

                    private readonly Bindable<int?> beatmapId = new Bindable<int?>();

                    private readonly Bindable<string> mods = new Bindable<string>(string.Empty);

                    private readonly Bindable<string> modParameters = new Bindable<string>(string.Empty);

                    private readonly Container drawableContainer;

                    public RoundBeatmapRow(TournamentRound team, RoundBeatmap beatmap)
                    {
                        Model = beatmap;

                        Margin = new MarginPadding(10);

                        RelativeSizeAxes = Axes.X;
                        AutoSizeAxes = Axes.Y;

                        Masking = true;
                        CornerRadius = 5;

                        InternalChildren = new Drawable[]
                        {
                            new Box
                            {
                                Colour = OsuColour.Gray(0.2f),
                                RelativeSizeAxes = Axes.Both,
                            },
                            new FillFlowContainer
                            {
                                Margin = new MarginPadding(5),
                                Padding = new MarginPadding { Right = 160 },
                                Spacing = new Vector2(5),
                                Direction = FillDirection.Horizontal,
                                AutoSizeAxes = Axes.Both,
                                Children = new Drawable[]
                                {
                                    new SettingsNumberBox
                                    {
                                        LabelText = "Beatmap ID",
                                        RelativeSizeAxes = Axes.None,
                                        Width = 180,
                                        Current = beatmapId,
                                    },
                                    new SettingsTextBox
                                    {
                                        LabelText = "Mods",
                                        RelativeSizeAxes = Axes.None,
                                        Width = 160,
                                        Current = mods,
                                    },
                                    new SettingsTextBox
                                    {
                                        LabelText = "Mod settings",
                                        RelativeSizeAxes = Axes.None,
                                        Width = 300,
                                        Current = modParameters,
                                    },
                                    drawableContainer = new Container
                                    {
                                        Size = new Vector2(100, 70),
                                    },
                                }
                            },
                            new DangerousSettingsButton
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                RelativeSizeAxes = Axes.None,
                                Width = 150,
                                Text = "Delete Beatmap",
                                Action = () =>
                                {
                                    Expire();
                                    team.Beatmaps.Remove(beatmap);
                                },
                            }
                        };
                    }

                    [BackgroundDependencyLoader]
                    private void load()
                    {
                        beatmapId.Value = Model.ID;
                        beatmapId.BindValueChanged(id =>
                        {
                            Model.ID = id.NewValue ?? 0;

                            if (id.NewValue != id.OldValue)
                                Model.Beatmap = null;

                            if (Model.Beatmap != null)
                            {
                                updatePanel();
                                return;
                            }

                            var req = new GetBeatmapRequest(new APIBeatmap { OnlineID = Model.ID });

                            req.Success += res =>
                            {
                                Model.Beatmap = new TournamentBeatmap(res);
                                updatePanel();
                            };

                            req.Failure += _ =>
                            {
                                Model.Beatmap = null;
                                updatePanel();
                            };

                            API.Queue(req);
                        }, true);

                        mods.Value = Model.Mods;
                        mods.BindValueChanged(modString =>
                        {
                            Model.Mods = modString.NewValue;
                            updatePanel();
                        });

                        modParameters.Value = serialiseModParameters(Model.ModParameters);
                        modParameters.BindValueChanged(text =>
                        {
                            Model.ModParameters = parseModParameters(text.NewValue);
                            updatePanel();
                        });
                    }

                    /// <summary>
                    /// Serialise <see cref="RoundBeatmap.ModParameters"/> into compact JSON
                    /// (e.g. <c>{"DT":{"speed_change":1.5}}</c>). Matches the on-disk shape in
                    /// <c>bracket.json</c>, so the textbox doubles as a copy/paste sink for that file.
                    /// </summary>
                    private static string serialiseModParameters(Dictionary<string, Dictionary<string, object>> parameters)
                    {
                        if (parameters.Count == 0)
                            return string.Empty;

                        return JsonConvert.SerializeObject(parameters);
                    }

                    /// <summary>
                    /// Parse the textbox content as a JSON object of <c>{acronym: {setting: value}}</c>.
                    /// Newtonsoft lands numeric values as <c>long</c>/<c>double</c>, booleans as
                    /// <c>bool</c>, strings as strings — all coerced downstream by
                    /// <see cref="osu.Game.Rulesets.Mods.Mod.CopyAdjustedSetting"/>. Invalid JSON
                    /// returns an empty dictionary so a typo doesn't blow up the editor; the user
                    /// fixes the JSON and the next commit re-renders the panel.
                    /// </summary>
                    internal static Dictionary<string, Dictionary<string, object>> parseModParameters(string text)
                    {
                        if (string.IsNullOrWhiteSpace(text))
                            return new Dictionary<string, Dictionary<string, object>>();

                        try
                        {
                            return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(text)
                                   ?? new Dictionary<string, Dictionary<string, object>>();
                        }
                        catch (JsonException)
                        {
                            return new Dictionary<string, Dictionary<string, object>>();
                        }
                    }

                    private void updatePanel() => Schedule(() =>
                    {
                        drawableContainer.Clear();

                        if (Model.Beatmap != null)
                        {
                            drawableContainer.Child = new TournamentBeatmapPanel(Model)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Width = 300
                            };
                        }
                    });
                }
            }
        }

        protected override RoundRow CreateDrawable(TournamentRound model) => new RoundRow(model);
    }
}
