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
    }
}
