// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Tournament.Configuration;
using osu.Game.Tournament.IPC;
using osu.Game.Tournament.Models;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Bridges <see cref="RemoteControlHandler"/> and <see cref="RemoteControlListener"/> to the
    /// live tournament client. All game-state mutations are dispatched onto the framework
    /// update thread via the component's <see cref="Drawable.Scheduler"/>.
    /// </summary>
    public partial class TournamentRemoteControl : Component
    {
        private RemoteControlListener? listener;

        [Resolved]
        private TournamentSceneManager sceneManager { get; set; } = null!;

        [Resolved]
        private LadderInfo ladder { get; set; } = null!;

        [Resolved]
        private MatchIPCInfo ipc { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            using var config = new TournamentConfigManager(storage);

            if (!config.Get<bool>(StorageConfig.RemoteControlEnabled))
                return;

            string bindAddress = config.Get<string>(StorageConfig.RemoteControlBindAddress);
            int port = config.Get<int>(StorageConfig.RemoteControlPort);

            var multiplayerIpc = ipc as MultiplayerMatchIPCInfo;

            var callbacks = new RemoteControlHandler.Callbacks
            {
                SwitchScreen = type => dispatch(() =>
                {
                    sceneManager.SetScreen(type);
                    return true;
                }),

                IncrementMatchScore = side => dispatch<int?>(() =>
                {
                    var match = ladder.CurrentMatch.Value;
                    if (match == null)
                        return null;

                    if (side == "red")
                    {
                        int newValue = (match.Team1Score.Value ?? 0) + 1;
                        match.Team1Score.Value = newValue;
                        return newValue;
                    }
                    else
                    {
                        int newValue = (match.Team2Score.Value ?? 0) + 1;
                        match.Team2Score.Value = newValue;
                        return newValue;
                    }
                }),

                AcceptPendingInvite = () => dispatch(() =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.InviteResult.NotAvailable;
                    if (multiplayerIpc.PendingInvite.Value == null)
                        return RemoteControlHandler.InviteResult.NoInvite;

                    multiplayerIpc.AcceptPendingInvite();
                    return RemoteControlHandler.InviteResult.Accepted;
                }),

                DismissPendingInvite = () => dispatch(() =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.InviteResult.NotAvailable;
                    if (multiplayerIpc.PendingInvite.Value == null)
                        return RemoteControlHandler.InviteResult.NoInvite;

                    multiplayerIpc.DismissPendingInvite();
                    return RemoteControlHandler.InviteResult.Accepted;
                }),

                Connect = (roomId, password) => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    try
                    {
                        await multiplayerIpc.Connect(roomId, password).ConfigureAwait(false);
                        return RemoteControlHandler.ConnectionResult.Ok;
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[RemoteControl] Connect failed: {e.Message}", LoggingTarget.Runtime, LogLevel.Important);
                        return RemoteControlHandler.ConnectionResult.WrongState;
                    }
                }),

                Disconnect = () => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (!multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    await multiplayerIpc.Disconnect().ConfigureAwait(false);
                    return RemoteControlHandler.ConnectionResult.Ok;
                }),

                Reconnect = () => dispatchAsync(async () =>
                {
                    if (multiplayerIpc == null)
                        return RemoteControlHandler.ConnectionResult.NotAvailable;
                    if (!multiplayerIpc.IsConnected.Value)
                        return RemoteControlHandler.ConnectionResult.WrongState;

                    await multiplayerIpc.Reconnect().ConfigureAwait(false);
                    return RemoteControlHandler.ConnectionResult.Ok;
                }),

                GetStatus = () => dispatch(() => buildStatus(multiplayerIpc)),
            };

            var handler = new RemoteControlHandler(callbacks);

            listener = new RemoteControlListener(bindAddress, port, handler);
            listener.Start();
        }

        private StatusSnapshot buildStatus(MultiplayerMatchIPCInfo? multiplayerIpc)
        {
            string? currentScreen = sceneManager.CurrentScreen?.GetType().Name;

            MultiplayerSnapshot mp;

            if (multiplayerIpc == null)
            {
                mp = MultiplayerSnapshot.Unavailable();
            }
            else
            {
                PendingInviteSnapshot? invite = multiplayerIpc.PendingInvite.Value == null
                    ? null
                    : new PendingInviteSnapshot(multiplayerIpc.PendingInvite.Value.RoomId, multiplayerIpc.PendingInvite.Value.InviterName);

                mp = new MultiplayerSnapshot(
                    Available: true,
                    Connected: multiplayerIpc.IsConnected.Value,
                    RoomId: multiplayerIpc.ConnectedRoomId.Value,
                    PendingInvite: invite,
                    TourneyState: multiplayerIpc.State.Value.ToString());
            }

            MatchSnapshot? match = null;

            if (ladder.CurrentMatch.Value is TournamentMatch current)
            {
                match = new MatchSnapshot(
                    Team1Score: current.Team1Score.Value,
                    Team2Score: current.Team2Score.Value,
                    Team1Acronym: current.Team1.Value?.Acronym.Value,
                    Team2Acronym: current.Team2.Value?.Acronym.Value);
            }

            return new StatusSnapshot(currentScreen, mp, match);
        }

        private Task<T> dispatch<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Schedule(() =>
            {
                try
                {
                    tcs.SetResult(action());
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            });

            return tcs.Task;
        }

        private Task<T> dispatchAsync<T>(Func<Task<T>> action)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            Schedule(() =>
            {
                action().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        tcs.SetException(t.Exception!.GetBaseException());
                    else
                        tcs.SetResult(t.GetResultSafely());
                });
            });

            return tcs.Task;
        }

        protected override void Dispose(bool isDisposing)
        {
            listener?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
