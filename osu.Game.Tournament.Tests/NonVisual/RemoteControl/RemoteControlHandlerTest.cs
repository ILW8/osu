// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Tournament.RemoteControl;

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
    }
}
