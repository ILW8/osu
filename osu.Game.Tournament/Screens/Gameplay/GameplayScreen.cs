// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Tournament.Components;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;
using osu.Game.Tournament.Screens.Gameplay.Components;
using osu.Game.Online.Multiplayer;
using osu.Game.Tournament.Screens.MapPool;
using osu.Game.Tournament.Screens.TeamWin;
using osuTK.Graphics;

namespace osu.Game.Tournament.Screens.Gameplay
{
    public partial class GameplayScreen : BeatmapInfoScreen
    {
        private readonly BindableBool warmup = new BindableBool();

        public readonly Bindable<TourneyState> State = new Bindable<TourneyState>();
        private MatchIPCInfo ipc = null!;

        [Resolved]
        private TournamentSceneManager? sceneManager { get; set; }

        [Resolved]
        private TournamentMatchChatDisplay chat { get; set; } = null!;

        private Container chroma = null!;
        private Container chromaOuter = null!;
        private ControlPanel controlPanel = null!;
        private TournamentGameplayDisplay? gameplayDisplay;

        [BackgroundDependencyLoader]
        private void load(MatchIPCInfo ipc)
        {
            this.ipc = ipc;

            LabelledSwitchButton chatToggle;

            AddRangeInternal(new Drawable[]
            {
                new TourneyVideo("gameplay")
                {
                    Loop = true,
                    RelativeSizeAxes = Axes.Both,
                },
                header = new MatchHeader
                {
                    ShowLogo = false,
                    ShowMatchRound = false,
                },
                chromaOuter = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Y = 110,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Children = new[]
                    {
                        chroma = new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Height = 512,
                            Children = new Drawable[]
                            {
                                new ChromaArea
                                {
                                    Name = "Left chroma",
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0.5f,
                                },
                                new ChromaArea
                                {
                                    Name = "Right chroma",
                                    RelativeSizeAxes = Axes.Both,
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Width = 0.5f,
                                }
                            }
                        },
                    }
                },
                scoreDisplay = new TournamentMatchScoreDisplay
                {
                    Y = -147,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.TopCentre,
                },
                controlPanel = new ControlPanel
                {
                    Children = new Drawable[]
                    {
                        new LabelledSwitchButton
                        {
                            Label = "Warmup",
                            Current = warmup,
                        },
                        chatToggle = new LabelledSwitchButton
                        {
                            Label = "Show chat",
                        },
                        new SettingsSlider<int>
                        {
                            LabelText = "Chroma width",
                            Current = LadderInfo.ChromaKeyWidth,
                            KeyboardStep = 1,
                        },
                        new SettingsSlider<int>
                        {
                            LabelText = "Players per team",
                            Current = LadderInfo.PlayersPerTeam,
                            KeyboardStep = 1,
                        },
                    }
                }
            });

            if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)
            {
                // Add gameplay display as a sibling of the UI audio container
                // (not a child) so its hitsounds bypass the UI sample muting.
                gameplayDisplay = new TournamentGameplayDisplay(multiplayerIpc)
                {
                    Alpha = 0,
                };

                // Position the gameplay display to match the chroma area exactly.
                chromaOuter.Add(new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Height = 512,
                    Child = gameplayDisplay,
                });

                controlPanel.Add(new SettingsSlider<int>
                {
                    LabelText = "Visible players",
                    Current = gameplayDisplay.VisibleSlotCount,
                    KeyboardStep = 1,
                });

                controlPanel.AddRange(new Drawable[]
                {
                    new ControlPanel.Spacer(),
                    new TourneyButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Text = "Force Idle state",
                        Action = forceLobbyState,
                    },
                });

                foreach (var child in chroma.Children.OfType<ChromaArea>())
                    child.Hide();

                multiplayerIpc.IsConnected.BindValueChanged(connected =>
                {
                    if (connected.NewValue)
                        gameplayDisplay.FadeIn(200);
                    else
                        gameplayDisplay.FadeOut(200);
                }, true);

                // Add volume sliders for multiplayer spectating.
                addVolumeControls();
            }

            State.BindValueChanged(state => chatToggle.Current.Value = State.Value == TourneyState.Idle, true);
            chatToggle.Current.BindValueChanged(v => State.Value = v.NewValue ? TourneyState.Idle : TourneyState.Playing);

            LadderInfo.ChromaKeyWidth.BindValueChanged(width =>
            {
                chroma.Width = width.NewValue;

                if (gameplayDisplay?.Parent is Container gameplayOuter)
                    gameplayOuter.Width = width.NewValue;
            }, true);

            warmup.BindValueChanged(w => header.ShowScores = !w.NewValue, true);
        }

        /// <summary>
        /// Operator override that forces <see cref="TourneyState.Idle"/>. Skips ahead when the
        /// IPC-driven Ranking → Idle timer (<see cref="MultiplayerMatchIPCInfo.RANKING_TO_IDLE_DELAY_MS"/>)
        /// hasn't fired yet, or re-pokes consumers (lobby music, screen advance) when already Idle.
        /// </summary>
        private void forceLobbyState()
        {
            if (ipc.State.Value == TourneyState.Idle)
            {
                Logger.Log("[GameplayScreen] Manual lobby trigger: state already Idle, re-firing event");
                ipc.State.TriggerChange();
            }
            else
            {
                Logger.Log($"[GameplayScreen] Manual lobby trigger: forcing state {ipc.State.Value} -> Idle");
                ipc.State.Value = TourneyState.Idle;
            }
        }

        private void addVolumeControls()
        {
            controlPanel.AddRange(new Drawable[]
            {
                new ControlPanel.Spacer(),
                new TournamentSpriteText
                {
                    Text = "Volume",
                    Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 16),
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Master",
                    Current = LadderInfo.VolumeMaster,
                    KeyboardStep = 0.01f,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Music",
                    Current = LadderInfo.VolumeMusic,
                    KeyboardStep = 0.01f,
                },
                new SettingsSlider<double>
                {
                    LabelText = "Effects",
                    Current = LadderInfo.VolumeEffect,
                    KeyboardStep = 0.01f,
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            State.BindTo(ipc.State);
            State.BindValueChanged(_ => updateState(), true);
        }

        protected override void CurrentMatchChanged(ValueChangedEvent<TournamentMatch?> match)
        {
            base.CurrentMatchChanged(match);

            if (match.NewValue == null)
                return;

            warmup.Value = match.NewValue.Team1Score.Value + match.NewValue.Team2Score.Value == 0;
            scheduledScreenChange?.Cancel();
        }

        private ScheduledDelegate? scheduledScreenChange;
        private ScheduledDelegate? scheduledContract;

        private TournamentMatchScoreDisplay scoreDisplay = null!;

        private TourneyState lastState;
        private MatchHeader header = null!;

        private void contract()
        {
            if (!IsLoaded)
                return;

            scheduledContract?.Cancel();

            SongBar.Expanded = false;
            scoreDisplay.FadeOut(100);
            using (chat.BeginDelayedSequence(500))
                chat.Expand();
        }

        private void expand()
        {
            if (!IsLoaded)
                return;

            scheduledContract?.Cancel();

            chat.Contract();

            using (BeginDelayedSequence(300))
            {
                scoreDisplay.FadeIn(100);
                SongBar.Expanded = true;
            }
        }

        private void updateState()
        {
            Logger.Log($"[GameplayScreen] updateState: state={State.Value} lastState={lastState} " +
                       $"warmup={warmup.Value} autoProgress={LadderInfo.AutoProgressScreens.Value} " +
                       $"match={(CurrentMatch.Value == null ? "null" : "set")} " +
                       $"completed={CurrentMatch.Value?.Completed.Value.ToString() ?? "n/a"} " +
                       $"isLoaded={IsLoaded} isPresent={IsPresent}");

            try
            {
                scheduledScreenChange?.Cancel();

                if (State.Value == TourneyState.Ranking)
                {
                    if (warmup.Value || CurrentMatch.Value == null) return;

                    if (LadderInfo.CumulativeScore.Value)
                    {
                        int mapId = ipc.Beatmap.Value?.OnlineID ?? 0;

                        if (mapId > 0)
                        {
                            var roundMap = CurrentMatch.Value.Round.Value?.Beatmaps.FirstOrDefault(b => b.ID == mapId);

                            if (roundMap != null)
                            {
                                CurrentMatch.Value.MapScores[roundMap.SlotName] = new Tuple<long, long>(ipc.Score1.Value, ipc.Score2.Value);

                                var currentSet = MatchSet.FindSetByMapId(CurrentMatch.Value, mapId);

                                if (currentSet != null)
                                {
                                    bool setComplete = currentSet.IsTiebreaker
                                        ? mapId == currentSet.Map3Id.Value
                                        : mapId == currentSet.Map2Id.Value;

                                    if (setComplete)
                                    {
                                        var scores = currentSet.GetSetScores(CurrentMatch.Value);

                                        if (scores != null)
                                        {
                                            if (scores.Item1 > scores.Item2)
                                                CurrentMatch.Value.Team1Score.Value++;
                                            else
                                                CurrentMatch.Value.Team2Score.Value++;

                                            // LGA 2026 §3.6 first-to-PointsToWin (3 for BestOf 5). Nullable
                                            // comparison: null >= 3 is false, so unstarted matches can't auto-complete.
                                            int pointsToWin = CurrentMatch.Value.PointsToWin;
                                            if (CurrentMatch.Value.Team1Score.Value >= pointsToWin
                                                || CurrentMatch.Value.Team2Score.Value >= pointsToWin)
                                            {
                                                CurrentMatch.Value.Completed.Value = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        if (ipc.Score1.Value > ipc.Score2.Value)
                            CurrentMatch.Value.Team1Score.Value++;
                        else
                            CurrentMatch.Value.Team2Score.Value++;
                    }
                }

                switch (State.Value)
                {
                    case TourneyState.Idle:
                        contract();

                        if (LadderInfo.AutoProgressScreens.Value)
                        {
                            // Multiplayer IPC reaches Idle via a fixed timer in MultiplayerMatchIPCInfo
                            // (RANKING_TO_IDLE_DELAY_MS) that already provides the post-results viewing
                            // window, so we don't stack another delay on top — and aligning the screen
                            // advance with the state transition keeps it synchronous with
                            // TournamentLobbyMusic resuming on the same Idle tick. File-based IPC keeps
                            // the historical 4s delay so the chat/score panel has time to collapse.
                            float delay_before_progression = ipc is MultiplayerMatchIPCInfo ? 0 : 4000;

                            // if we've returned to idle and the last screen was ranking
                            // we should automatically proceed after a short delay
                            if (lastState == TourneyState.Ranking && !warmup.Value)
                            {
                                Type? nextScreen = CurrentMatch.Value?.Completed.Value switch
                                {
                                    true => typeof(TeamWinScreen),
                                    false => typeof(MapPoolScreen),
                                    _ => null,
                                };

                                Logger.Log($"[GameplayScreen] Idle: scheduling advance to {nextScreen?.Name ?? "(null)"}");

                                if (nextScreen != null)
                                {
                                    scheduledScreenChange = Scheduler.AddDelayed(() =>
                                    {
                                        Logger.Log($"[GameplayScreen] Advance timer fired, switching to {nextScreen.Name}");

                                        // Reset the per-map score bar for the next round. Teardown of the
                                        // player grid is intentionally deferred to the next round's
                                        // LoadRequested — the grid is hidden behind MapPoolScreen anyway,
                                        // and TournamentGameplayDisplay.teardownGameplay() calls
                                        // masterClockContainer.Stop() which would stop the shared
                                        // WorkingBeatmap.Track that the lobby-music MusicController has
                                        // just started playing on the same Idle tick.
                                        if (ipc is MultiplayerMatchIPCInfo)
                                        {
                                            ipc.Score1.Value = 0;
                                            ipc.Score2.Value = 0;
                                        }

                                        sceneManager?.SetScreen(nextScreen);
                                    }, delay_before_progression);
                                }
                            }
                        }

                        break;

                    case TourneyState.Ranking:
                        scheduledContract = Scheduler.AddDelayed(contract, 10000);
                        break;

                    default:
                        expand();
                        break;
                }
            }
            finally
            {
                lastState = State.Value;
            }
        }

        public override void Hide()
        {
            scheduledScreenChange?.Cancel();
            base.Hide();
        }

        public override void Show()
        {
            updateState();

            // Replay spectator states the display's binding missed while this screen was hidden.
            gameplayDisplay?.RefreshActiveSpectators();

            base.Show();
        }

        private partial class ChromaArea : CompositeDrawable
        {
            [Resolved]
            private LadderInfo ladder { get; set; } = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                // chroma key area for stable gameplay
                Colour = new Color4(0, 255, 0, 255);

                ladder.PlayersPerTeam.BindValueChanged(performLayout, true);
            }

            private void performLayout(ValueChangedEvent<int> playerCount)
            {
                switch (playerCount.NewValue)
                {
                    case 3:
                        InternalChildren = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0.5f,
                                Height = 0.5f,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Height = 0.5f,
                            },
                        };
                        break;

                    default:
                        InternalChild = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                        };
                        break;
                }
            }
        }
    }
}
