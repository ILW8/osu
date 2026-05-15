// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Drawing;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osuTK;

namespace osu.Game.Tournament.Screens.Setup
{
    public partial class SetupScreen : TournamentScreen
    {
        private FillFlowContainer fillFlow = null!;

        private LoginOverlay? loginOverlay;
        private ResolutionSelector resolution = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private StableInfo stableInfo { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        [Resolved]
        private Storage storage { get; set; } = null!;

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();
        private Bindable<Size> windowSize = null!;

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager frameworkConfig)
        {
            windowSize = frameworkConfig.GetBindable<Size>(FrameworkSetting.WindowedSize);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourProvider.Background5,
                },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = fillFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(10),
                        Spacing = new Vector2(10),
                    },
                },
            };

            localUser.BindTo(api.LocalUser);
            localUser.BindValueChanged(_ => Schedule(reload));
            stableInfo.OnStableInfoSaved += () => Schedule(reload);
            reload();
        }

        [Resolved]
        private TournamentGameBase game { get; set; } = null!;

        private void reload()
        {
            bool isCurrentlyMultiplayer = ipc is MultiplayerMatchIPCInfo;

            var fileBasedIpc = ipc as FileBasedIPC;

            var restartButton = new TourneyButton
            {
                RelativeSizeAxes = Axes.X,
                Text = "Save and restart to apply",
                Alpha = LadderInfo.UseMultiplayerSpectating.Value != isCurrentlyMultiplayer ? 1 : 0,
                Action = () =>
                {
                    game.SaveChanges();
                    game.AttemptExit();
                },
            };

            var children = new List<Drawable>
            {
                new LabelledSwitchButton
                {
                    Label = "Use multiplayer spectating",
                    Description = "When enabled, the overlay connects to a multiplayer room for match data instead of reading from the stable client's IPC files.",
                    Current = LadderInfo.UseMultiplayerSpectating,
                },
                new LabelledSwitchButton
                {
                    Label = "1v1 mode",
                    Description = "Text elements referring to \"Team\"s will be updated to \"Player\"s and team players lists will be hidden.",
                    Current = LadderInfo.Use1V1Mode,
                },
                restartButton,
            };

            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
                children.Add(new MultiplayerRoomConnectionControls(multiplayerIpc));

            children.AddRange(new Drawable[]
            {
                new ActionableInfo
                {
                    Label = "Current IPC source",
                    ButtonText = "Change source",
                    Action = () => sceneManager?.SetScreen(new StablePathSelectScreen()),
                    Value = fileBasedIpc?.IPCStorage?.GetFullPath(string.Empty) ?? "Not found",
                    Failing = fileBasedIpc?.IPCStorage == null,
                    Description = "The osu!stable installation which is currently being used as a data source. If a source is not found, make sure you have created an empty ipc.txt in your stable cutting-edge installation.",
                    Alpha = LadderInfo.UseMultiplayerSpectating.Value ? 0 : 1,
                },
                new ActionableInfo
                {
                    Label = "Current user",
                    ButtonText = "Change sign-in",
                    Action = () =>
                    {
                        api.Logout();

                        if (loginOverlay == null)
                        {
                            AddInternal(loginOverlay = new LoginOverlay
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                            });
                        }

                        loginOverlay.State.Value = Visibility.Visible;
                    },
                    Value = api.LocalUser.Value.Username,
                    Failing = api.IsLoggedIn != true,
                    Description = "In order to access the API and display metadata, signing in is required."
                },
                new LabelledDropdown<RulesetInfo?>(padded: true)
                {
                    Label = "Ruleset",
                    Description = "Decides what stats are displayed and which ranks are retrieved for players. This requires a restart to reload data for an existing bracket.",
                    Items = rulesets.AvailableRulesets,
                    Current = LadderInfo.Ruleset,
                    DropdownWidth = 0.5f,
                },
                new TournamentSwitcher
                {
                    Label = "Current tournament",
                    Description = "Changes the background videos and bracket to match the selected tournament. This requires a restart to apply changes.",
                },
                resolution = new ResolutionSelector
                {
                    Label = "Stream area resolution",
                    ButtonText = "Set height",
                    Action = height =>
                    {
                        windowSize.Value = new Size((int)(height * aspect_ratio / TournamentSceneManager.STREAM_AREA_WIDTH * TournamentSceneManager.REQUIRED_WIDTH), height);
                    }
                },
                new LabelledSwitchButton
                {
                    Label = "Auto advance screens",
                    Description = "Screens will progress automatically from gameplay -> results -> map pool",
                    Current = LadderInfo.AutoProgressScreens,
                },
                new LabelledSwitchButton
                {
                    Label = "Display team seeds",
                    Description = "Team seeds will display alongside each team at the top in gameplay/map pool screens.",
                    Current = LadderInfo.DisplayTeamSeeds,
                },
                new LabelledSwitchButton
                {
                    Label = "Use cumulative score",
                    Description = "Instead of a single point per map won, set winners are decided by total cumulative score across the maps in the set.",
                    Current = LadderInfo.CumulativeScore,
                },
                new SettingsSlider<int>
                {
                    LabelText = "Multiplayer IPC write interval (ms)",
                    Current = LadderInfo.IPCWriteIntervalMilliseconds,
                    KeyboardStep = 10,
                    Alpha = LadderInfo.UseMultiplayerSpectating.Value ? 1 : 0,
                },
                new ActionableInfo
                {
                    Label = "Multiplayer IPC output path",
                    ButtonText = "Open folder",
                    Action = () => storage.GetStorageForDirectory(MultiplayerIPCWriter.IPC_DIRECTORY).PresentExternally(),
                    Value = storage.GetFullPath(
                        Path.Combine(MultiplayerIPCWriter.IPC_DIRECTORY, MultiplayerIPCWriter.IPC_FILENAME)),
                    Description = "External overlays and scoreboards can poll this file for live room state.",
                    Alpha = LadderInfo.UseMultiplayerSpectating.Value ? 1 : 0,
                },
                new LabelledSwitchButton
                {
                    Label = "Mute UI sounds",
                    Description = "Mutes hover and click sounds from buttons and controls. Gameplay hitsounds are unaffected.",
                    Current = LadderInfo.MuteUISounds,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Master volume",
                    Current = LadderInfo.VolumeMaster,
                    KeyboardStep = 0.01f,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Music volume",
                    Current = LadderInfo.VolumeMusic,
                    KeyboardStep = 0.01f,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Effects volume",
                    Current = LadderInfo.VolumeEffect,
                    KeyboardStep = 0.01f,
                },
            });

            fillFlow.Children = children;

            LadderInfo.UseMultiplayerSpectating.BindValueChanged(v =>
            {
                if (v.NewValue != isCurrentlyMultiplayer)
                    restartButton.FadeIn(200);
                else
                    restartButton.FadeOut(200);
            });
        }

        private const float aspect_ratio = 16f / 9f;

        protected override void Update()
        {
            base.Update();

            resolution.Value = $"{ScreenSpaceDrawQuad.Width:N0}x{ScreenSpaceDrawQuad.Height:N0}";
        }
    }
}
