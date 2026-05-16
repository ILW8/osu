// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Extensions;
using osu.Framework.Logging;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Hosts a <see cref="HttpListener"/> on a single dedicated thread. Calls
    /// <see cref="RemoteControlHandler.Handle"/> for each request and writes the response.
    /// Bind failures are logged and leave the listener in a non-running state — they never throw.
    /// </summary>
    public sealed class RemoteControlListener : IDisposable
    {
        private readonly string bindAddress;
        private readonly int port;
        private readonly RemoteControlHandler handler;

        private HttpListener? listener;
        private Thread? loopThread;
        private CancellationTokenSource? cancellation;

        public bool IsRunning { get; private set; }

        public RemoteControlListener(string bindAddress, int port, RemoteControlHandler handler)
        {
            this.bindAddress = bindAddress;
            this.port = port;
            this.handler = handler;
        }

        public void Start()
        {
            if (IsRunning)
                return;

            listener = new HttpListener();
            listener.Prefixes.Add($"http://{bindAddress}:{port}/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException e)
            {
                Logger.Log($"[RemoteControl] Failed to bind to {bindAddress}:{port}: {e.Message}", LoggingTarget.Runtime, LogLevel.Error);
                listener = null;
                return;
            }

            cancellation = new CancellationTokenSource();
            loopThread = new Thread(loop) { IsBackground = true, Name = $"RemoteControl-{port}" };
            loopThread.Start();
            IsRunning = true;

            Logger.Log($"[RemoteControl] Listening on http://{bindAddress}:{port}/", LoggingTarget.Runtime, LogLevel.Important);
        }

        private void loop()
        {
            while (!cancellation!.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = listener!.GetContext();
                }
                catch (HttpListenerException)
                {
                    // listener was stopped — exit cleanly.
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    handleContext(context);
                }
                catch (Exception e)
                {
                    Logger.Log($"[RemoteControl] Unhandled exception: {e}", LoggingTarget.Runtime, LogLevel.Important);

                    try
                    {
                        writeResponse(context, RemoteControlResponse.Error(500, e.Message));
                    }
                    catch
                    {
                        // best-effort — the connection may already be torn down.
                    }
                }
            }
        }

        private void handleContext(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod;
            string path = context.Request.Url?.PathAndQuery ?? "/";
            string? body = null;

            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                body = reader.ReadToEnd();
            }

            // Block the listener thread on the handler. The handler dispatches to the
            // update thread internally via its callbacks; it must complete (or time out)
            // before we can write the response.
            var handleTask = handler.Handle(method, path, body);

            if (!handleTask.Wait(2500))
            {
                writeResponse(context, RemoteControlResponse.Error(504, "request timed out"));
                return;
            }

            writeResponse(context, handleTask.GetResultSafely());
        }

        private static void writeResponse(HttpListenerContext context, RemoteControlResponse response)
        {
            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";

            byte[] bytes = Encoding.UTF8.GetBytes(response.JsonBody);
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }

        public void Dispose()
        {
            if (!IsRunning)
            {
                listener?.Close();
                return;
            }

            IsRunning = false;
            cancellation?.Cancel();

            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch
            {
                // already torn down.
            }

            loopThread?.Join(1000);
            cancellation?.Dispose();
        }
    }
}
