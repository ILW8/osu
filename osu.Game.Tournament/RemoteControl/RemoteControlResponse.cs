// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Tournament.RemoteControl
{
    public readonly record struct RemoteControlResponse(int StatusCode, string JsonBody)
    {
        public static RemoteControlResponse Ok() => new RemoteControlResponse(200, @"{""ok"":true}");

        public static RemoteControlResponse OkWith(string jsonBody) => new RemoteControlResponse(200, jsonBody);

        public static RemoteControlResponse Error(int statusCode, string message) =>
            new RemoteControlResponse(statusCode, $@"{{""ok"":false,""error"":{Newtonsoft.Json.JsonConvert.ToString(message)}}}");
    }
}
