// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online
{
    public class ProductionEndpointConfiguration : EndpointConfiguration
    {
        public ProductionEndpointConfiguration()
        {
            WebsiteRootUrl = APIEndpointUrl = @"http://wph.btmc.live:8080";
            APIClientSecret = @"hxr4AmDp7p9O6enQvSFeeKjctO1tHHodtRZnX25i";
            APIClientID = "1";
            SpectatorEndpointUrl = $@"{APIEndpointUrl}/signalr/spectator";
            MultiplayerEndpointUrl = $@"{APIEndpointUrl}/signalr/multiplayer";
            MetadataEndpointUrl = $@"{APIEndpointUrl}/signalr/metadata";
        }
    }
}
