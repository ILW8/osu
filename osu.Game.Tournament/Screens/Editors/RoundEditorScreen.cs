// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
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
                                        Width = 200,
                                        Current = beatmapId,
                                    },
                                    new SettingsTextBox
                                    {
                                        LabelText = "Mods",
                                        RelativeSizeAxes = Axes.None,
                                        Width = 200,
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
                    /// Serialise a <see cref="RoundBeatmap.ModParameters"/> dictionary into the textbox format.
                    /// One line per setting: <c>ACRONYM.setting=value</c>.
                    /// </summary>
                    private static string serialiseModParameters(Dictionary<string, Dictionary<string, object>> parameters)
                    {
                        if (parameters.Count == 0)
                            return string.Empty;

                        var lines = new List<string>();
                        foreach (var (acronym, settings) in parameters)
                        {
                            foreach (var (key, value) in settings)
                                lines.Add($"{acronym}.{key}={value}");
                        }

                        return string.Join('\n', lines);
                    }

                    /// <summary>
                    /// Parse the textbox content. Each line is <c>ACRONYM.setting=value</c>; the value is
                    /// tried as <c>double</c>, then <c>bool</c>, then falls through as a raw string.
                    /// Malformed lines are silently skipped — free-form editor, iterate UI later if clunky.
                    /// </summary>
                    internal static Dictionary<string, Dictionary<string, object>> parseModParameters(string text)
                    {
                        var result = new Dictionary<string, Dictionary<string, object>>();

                        if (string.IsNullOrWhiteSpace(text))
                            return result;

                        foreach (string rawLine in text.Split('\n'))
                        {
                            string line = rawLine.Trim();
                            if (line.Length == 0)
                                continue;

                            int dot = line.IndexOf('.');
                            int eq = line.IndexOf('=');

                            if (dot <= 0 || eq <= dot + 1)
                                continue;

                            string acronym = line.Substring(0, dot).Trim();
                            string key = line.Substring(dot + 1, eq - dot - 1).Trim();
                            string rawValue = line.Substring(eq + 1).Trim();

                            if (acronym.Length == 0 || key.Length == 0)
                                continue;

                            object value;
                            if (double.TryParse(rawValue, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                                value = d;
                            else if (bool.TryParse(rawValue, out bool b))
                                value = b;
                            else
                                value = rawValue;

                            if (!result.TryGetValue(acronym, out var settings))
                            {
                                settings = new Dictionary<string, object>();
                                result[acronym] = settings;
                            }

                            settings[key] = value;
                        }

                        return result;
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
