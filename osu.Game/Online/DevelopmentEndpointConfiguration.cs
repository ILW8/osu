// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online
{
    public class DevelopmentEndpointConfiguration : EndpointConfiguration
    {
        public DevelopmentEndpointConfiguration()
        {
            WebsiteRootUrl = APIEndpointUrl = @"https://dev.ppy.sh";
            APIClientSecret = @"3LP2mhUrV89xxzD1YKNndXHEhWWCRLPNKioZ9ymT";
            APIClientID = "5";
            SpectatorEndpointUrl = @"http://localhost:8000/signalr/spectator";
            MultiplayerEndpointUrl = @"http://localhost:8000/signalr/multiplayer";
            MetadataEndpointUrl = @"http://localhost:8000/signalr/metadata";
        }
    }
}
