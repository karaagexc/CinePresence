using CinePresence.App.Services;
using DiscordRPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CinePresence.Tests;

public sealed class DiscordPayloadTests
{
    [Fact] public void WatchingPayloadUsesMillisecondsAndMediaDetails()
    {
        var payload = PresenceBuilder.Build(Fixture.Source(), Fixture.Movie() with { PosterUrl = "https://image.tmdb.org/t/p/w500/poster.jpg" }, Fixture.Now);
        var presence = DiscordPublisher.ToRichPresence(payload);
        Assert.Equal(ActivityType.Watching, presence.Type);
        Assert.Equal(StatusDisplayType.Details, presence.StatusDisplay);
        var json = JObject.Parse(JsonConvert.SerializeObject(presence));
        Assert.Equal(3, json["type"]!.Value<int>());
        Assert.Equal(Fixture.Now.AddSeconds(-100).ToUnixTimeMilliseconds(), json["timestamps"]!["start"]!.Value<long>());
        Assert.Equal(Fixture.Now.AddSeconds(900).ToUnixTimeMilliseconds(), json["timestamps"]!["end"]!.Value<long>());
        Assert.Equal(payload.PosterUrl, json["assets"]!["large_image"]!.Value<string>());
        Assert.Equal(payload.Url, json["buttons"]![0]!["url"]!.Value<string>());
    }
    [Fact] public void UnknownTimingOmitsTimestampObject()
    {
        var presence = DiscordPublisher.ToRichPresence(PresenceBuilder.Build(Fixture.Source() with { Duration = null }, Fixture.Movie(), Fixture.Now));
        Assert.Null(presence.Timestamps);
        Assert.Null(JObject.Parse(JsonConvert.SerializeObject(presence))["timestamps"]);
    }
}
