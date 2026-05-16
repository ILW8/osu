// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Tournament.RemoteControl;
using osu.Game.Tournament.Screens.Setup;

namespace osu.Game.Tournament.Tests.NonVisual.RemoteControl
{
    [TestFixture]
    public class RemoteControlHandlerTest
    {
        [Test]
        public async Task UnknownRoute_Returns404()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            var response = await handler.Handle("GET", "/nope", null);

            Assert.That(response.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task WrongMethod_Returns405()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            // /status is a known GET route; POST is not allowed.
            var response = await handler.Handle("POST", "/status", null);

            Assert.That(response.StatusCode, Is.EqualTo(405));
        }

        [Test]
        public async Task ScreenSwitch_KnownName_InvokesCallbackAndReturns200()
        {
            System.Type? captured = null;

            var callbacks = new RemoteControlHandler.Callbacks
            {
                SwitchScreen = type =>
                {
                    captured = type;
                    return Task.FromResult(true);
                },
            };

            var handler = new RemoteControlHandler(callbacks);

            var response = await handler.Handle("POST", "/screen/setup", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(captured, Is.EqualTo(typeof(SetupScreen)));
        }

        [Test]
        public async Task ScreenSwitch_UnknownName_Returns400()
        {
            var callbacks = new RemoteControlHandler.Callbacks
            {
                SwitchScreen = _ => Task.FromResult(true),
            };

            var handler = new RemoteControlHandler(callbacks);

            var response = await handler.Handle("POST", "/screen/banana", null);

            Assert.That(response.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task ScreenSwitch_WrongMethod_Returns405()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                SwitchScreen = _ => Task.FromResult(true),
            });

            var response = await handler.Handle("GET", "/screen/setup", null);

            Assert.That(response.StatusCode, Is.EqualTo(405));
        }

        [Test]
        public async Task ScoreIncrement_Red_InvokesCallbackAndReturns200()
        {
            string? side = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                IncrementMatchScore = team =>
                {
                    side = team;
                    return Task.FromResult<int?>(1);
                },
            });

            var response = await handler.Handle("POST", "/match/score/red/increment", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(side, Is.EqualTo("red"));
        }

        [Test]
        public async Task ScoreIncrement_Blue_InvokesCallbackAndReturns200()
        {
            string? side = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                IncrementMatchScore = team =>
                {
                    side = team;
                    return Task.FromResult<int?>(1);
                },
            });

            var response = await handler.Handle("POST", "/match/score/blue/increment", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(side, Is.EqualTo("blue"));
        }

        [Test]
        public async Task ScoreIncrement_NoCurrentMatch_Returns409()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                IncrementMatchScore = _ => Task.FromResult<int?>(null),
            });

            var response = await handler.Handle("POST", "/match/score/red/increment", null);

            Assert.That(response.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task InviteAccept_HappyPath_Returns200()
        {
            bool called = false;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                AcceptPendingInvite = () =>
                {
                    called = true;
                    return Task.FromResult(RemoteControlHandler.InviteResult.Accepted);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(called, Is.True);
        }

        [Test]
        public async Task InviteAccept_MultiplayerUnavailable_Returns503()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                AcceptPendingInvite = () => Task.FromResult(RemoteControlHandler.InviteResult.NotAvailable),
            });

            var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

            Assert.That(response.StatusCode, Is.EqualTo(503));
        }

        [Test]
        public async Task InviteAccept_NoInvite_Returns409()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                AcceptPendingInvite = () => Task.FromResult(RemoteControlHandler.InviteResult.NoInvite),
            });

            var response = await handler.Handle("POST", "/multiplayer/invite/accept", null);

            Assert.That(response.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task InviteDismiss_HappyPath_Returns200()
        {
            bool called = false;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                DismissPendingInvite = () =>
                {
                    called = true;
                    return Task.FromResult(RemoteControlHandler.InviteResult.Accepted);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/invite/dismiss", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(called, Is.True);
        }

        [Test]
        public async Task Connect_FromJsonBody_InvokesCallback()
        {
            long? roomId = null;
            string? password = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (r, p) =>
                {
                    roomId = r;
                    password = p;
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/connect", @"{""roomId"":42,""password"":""hunter2""}");

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(roomId, Is.EqualTo(42));
            Assert.That(password, Is.EqualTo("hunter2"));
        }

        [Test]
        public async Task Connect_FromQueryString_InvokesCallback()
        {
            long? roomId = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (r, _) =>
                {
                    roomId = r;
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(roomId, Is.EqualTo(99));
        }

        [Test]
        public async Task Connect_BodyOverridesQuery()
        {
            long? roomId = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (r, _) =>
                {
                    roomId = r;
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", @"{""roomId"":42}");

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(roomId, Is.EqualTo(42));
        }

        [Test]
        public async Task Connect_MissingRoomId_Returns400()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.Ok),
            });

            var response = await handler.Handle("POST", "/multiplayer/connect", null);

            Assert.That(response.StatusCode, Is.EqualTo(400));
        }

        [Test]
        public async Task Connect_AlreadyConnected_Returns409()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.WrongState),
            });

            var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

            Assert.That(response.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task Connect_NotAvailable_Returns503()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (_, _) => Task.FromResult(RemoteControlHandler.ConnectionResult.NotAvailable),
            });

            var response = await handler.Handle("POST", "/multiplayer/connect?roomId=99", null);

            Assert.That(response.StatusCode, Is.EqualTo(503));
        }

        [Test]
        public async Task Disconnect_HappyPath_Returns200()
        {
            bool called = false;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Disconnect = () =>
                {
                    called = true;
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/disconnect", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(called, Is.True);
        }

        [Test]
        public async Task Disconnect_NotConnected_Returns409()
        {
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Disconnect = () => Task.FromResult(RemoteControlHandler.ConnectionResult.WrongState),
            });

            var response = await handler.Handle("POST", "/multiplayer/disconnect", null);

            Assert.That(response.StatusCode, Is.EqualTo(409));
        }

        [Test]
        public async Task Reconnect_HappyPath_Returns200()
        {
            bool called = false;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Reconnect = () =>
                {
                    called = true;
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            var response = await handler.Handle("POST", "/multiplayer/reconnect", null);

            Assert.That(response.StatusCode, Is.EqualTo(200));
            Assert.That(called, Is.True);
        }
    }
}
