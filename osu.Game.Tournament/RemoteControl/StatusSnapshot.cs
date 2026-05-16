// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Tournament.RemoteControl
{
    /// <summary>
    /// Immutable snapshot returned by GET /status. Construction happens on the framework
    /// update thread; serialization happens on the listener thread.
    /// </summary>
    public record StatusSnapshot(
        [property: JsonProperty("currentScreen")] string? CurrentScreen,
        [property: JsonProperty("multiplayer")] MultiplayerSnapshot Multiplayer,
        [property: JsonProperty("match")] MatchSnapshot? Match
    );

    public record MultiplayerSnapshot(
        [property: JsonProperty("available")] bool Available,
        [property: JsonProperty("connected", NullValueHandling = NullValueHandling.Ignore)] bool? Connected,
        [property: JsonProperty("roomId", NullValueHandling = NullValueHandling.Ignore)] long? RoomId,
        [property: JsonProperty("pendingInvite", NullValueHandling = NullValueHandling.Ignore)] PendingInviteSnapshot? PendingInvite,
        [property: JsonProperty("tourneyState", NullValueHandling = NullValueHandling.Ignore)] string? TourneyState
    )
    {
        public static MultiplayerSnapshot Unavailable() => new MultiplayerSnapshot(false, null, null, null, null);
    }

    public record PendingInviteSnapshot(
        [property: JsonProperty("roomId")] long RoomId,
        [property: JsonProperty("inviter")] string Inviter
    );

    public record MatchSnapshot(
        [property: JsonProperty("team1Score")] int? Team1Score,
        [property: JsonProperty("team2Score")] int? Team2Score,
        [property: JsonProperty("team1Acronym")] string? Team1Acronym,
        [property: JsonProperty("team2Acronym")] string? Team2Acronym
    );
}
