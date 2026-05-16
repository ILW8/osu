// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Tournament.RemoteControl;

namespace osu.Game.Tournament.Tests.NonVisual.RemoteControl
{
    [TestFixture]
    public class RemoteControlListenerTest
    {
        private static int pickFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Test]
        public async Task RoundTrip_UnknownRoute_Returns404()
        {
            int port = pickFreePort();
            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());

            using var listener = new RemoteControlListener("127.0.0.1", port, handler);
            listener.Start();

            using var client = new HttpClient();
            var response = await client.GetAsync($"http://127.0.0.1:{port}/nope");

            Assert.That((int)response.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task RoundTrip_PostJsonBody_IsPassedToHandler()
        {
            int port = pickFreePort();
            string? receivedBody = null;

            var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks
            {
                Connect = (roomId, _) =>
                {
                    receivedBody = $"roomId={roomId}";
                    return Task.FromResult(RemoteControlHandler.ConnectionResult.Ok);
                },
            });

            using var listener = new RemoteControlListener("127.0.0.1", port, handler);
            listener.Start();

            using var client = new HttpClient();
            var response = await client.PostAsync(
                $"http://127.0.0.1:{port}/multiplayer/connect",
                new StringContent(@"{""roomId"":99}", Encoding.UTF8, "application/json"));

            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(receivedBody, Is.EqualTo("roomId=99"));
        }

        [Test]
        public void Start_FailedBind_DoesNotThrow()
        {
            // Occupy a port, then try to bind a listener to the same port.
            int port = pickFreePort();

            var occupier = new TcpListener(IPAddress.Loopback, port);
            occupier.Start();

            try
            {
                var handler = new RemoteControlHandler(new RemoteControlHandler.Callbacks());
                using var listener = new RemoteControlListener("127.0.0.1", port, handler);

                Assert.DoesNotThrow(() => listener.Start());
                Assert.That(listener.IsRunning, Is.False);
            }
            finally
            {
                occupier.Stop();
            }
        }
    }
}
